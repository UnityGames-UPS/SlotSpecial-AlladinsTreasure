using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// The free games' screen (ATFreeGamesBonus.md §4, §5.1, §6.2, §8): the full-screen transition in and
/// out, the two backgrounds, the top panel above the slot machine ("N FREE GAMES REMAINING" / "LAST
/// SPIN" / "FEATURE COMPLETE") and the TOTAL PRIZE panel. The popups are FeaturePopupView's; the frames
/// and end orbs are FramesView's.
///
/// View layer only. GameManager drives this and is called back when the transition has covered the
/// screen and when it has finished; this never calls into the game loop. Every reference is optional —
/// a missing one skips its part and still calls back.
/// </summary>
public class FreeGameView : MonoBehaviour
{
    [Header("Transition")]
    [Tooltip("The full-screen transition: an ImageAnimation playing its sprite frames once, the same going in and out. Switched on to play and off at its end; its own frame list holds the clip.")]
    [SerializeField] private ImageAnimation transition;
    [Tooltip("Seconds into the transition at which the screen is covered and the swap happens behind it (backgrounds, top panel, board). Never later than the clip's end.")]
    [SerializeField] private float transitionSwapDelay = 0.5f;
    [Tooltip("How long the transition lasts when it has no clip (or none is wired): the swap still happens, then it ends.")]
    [SerializeField] private float transitionFallbackDuration = 0f;

    [Header("Backgrounds")]
    [SerializeField] private GameObject baseGameBackground;
    [SerializeField] private GameObject freeGamesBackground;

    [Header("Top panel")]
    [Tooltip("The panel above the slot machine. On for the whole bonus, off in the base game.")]
    [SerializeField] private GameObject topPanel;
    [Tooltip("\"N FREE GAMES REMAINING\" — the state shown during the games.")]
    [SerializeField] private GameObject remainingState;
    [Tooltip("N, the games remaining (the current one included).")]
    [SerializeField] private TMPro.TMP_Text remainingCountText;
    [Tooltip("\"LAST SPIN\" — shown instead of \"1 FREE GAMES REMAINING\".")]
    [SerializeField] private GameObject lastSpinState;
    [Tooltip("\"FEATURE COMPLETE\".")]
    [SerializeField] private GameObject featureCompleteState;
    [Tooltip("On: the remaining count is written as sprite digits. Off: plain text.")]
    [SerializeField] private bool countUsesSpriteDigits;

    [Header("TOTAL PRIZE panel")]
    [Tooltip("The TOTAL PRIZE panel — replaces FEATURE COMPLETE for the prize reveal and pops in with the orbs.")]
    [SerializeField] private RectTransform totalPrizePanel;
    [Tooltip("Its value — blank until the first orb's count-up.")]
    [SerializeField] private TMPro.TMP_Text totalPrizeValueText;
    [Tooltip("On: the value is written as sprite digits. Off: plain text, \"12.50\".")]
    [SerializeField] private bool prizeUsesSpriteDigits;
    [SerializeField] private float totalPrizePopDuration = 0.4f;
    [SerializeField] private float totalPrizePopOvershoot = 1.7f;

    private readonly List<Tween> tweens = new List<Tween>();
    private Tween prizeCountTween;
    private double totalPrizeShown;
    private Vector3 totalPrizeHomeScale = Vector3.one;
    private bool totalPrizeHomeRead;

    #region Called by GameManager

    /// <summary>The base game's look: base background, no top panel, no TOTAL PRIZE, no transition.</summary>
    internal void ResetToBaseGame()
    {
        KillTweens();
        if (transition != null)
        {
            transition.onLoopComplete = null;
            transition.StopAnimation();
            transition.gameObject.SetActive(false);
        }
        ShowFreeGamesLook(false);
    }

    /// <summary>
    /// The transition (§4, §8). onCovered fires transitionSwapDelay into it — the screen is covered, the
    /// moment to swap the look behind it; onDone fires at its end. With no clip, onCovered fires at
    /// once and onDone after transitionFallbackDuration.
    /// </summary>
    internal void PlayTransition(Action onCovered, Action onDone)
    {
        bool covered = false;
        bool done = false;

        void Cover()
        {
            if (covered) return;
            covered = true;
            onCovered?.Invoke();
        }

        void Finish()
        {
            if (done) return;
            done = true;
            Cover();   // never end without the swap
            if (transition != null)
            {
                transition.onLoopComplete = null;
                transition.StopAnimation();
                transition.gameObject.SetActive(false);
            }
            onDone?.Invoke();
        }

        bool hasClip = transition != null && transition.textureArray != null && transition.textureArray.Count > 0;
        if (!hasClip)
        {
            Cover();
            if (transitionFallbackDuration > 0f) tweens.Add(DOVirtual.DelayedCall(transitionFallbackDuration, Finish));
            else Finish();
            return;
        }

        transition.doLoopAnimation = false;
        transition.onLoopComplete = loop => Finish();
        transition.gameObject.SetActive(true);
        transition.StartAnimation();

        tweens.Add(DOVirtual.DelayedCall(Mathf.Max(0f, transitionSwapDelay), Cover));
    }

    /// <summary>
    /// The free games' look on or off (§4, §8): the background swapped, the top panel shown or hidden.
    /// Called while the transition covers the screen. Off also takes TOTAL PRIZE down.
    /// </summary>
    internal void ShowFreeGamesLook(bool on)
    {
        if (baseGameBackground != null) baseGameBackground.SetActive(!on);
        if (freeGamesBackground != null) freeGamesBackground.SetActive(on);
        if (topPanel != null) topPanel.SetActive(on);

        if (!on)
        {
            ShowOnlyState(null);
            HideTotalPrize();
        }
    }

    /// <summary>
    /// The count (§5.1): "N FREE GAMES REMAINING", or "LAST SPIN" when N is 1. N includes the game being
    /// played. Swapped instantly.
    /// </summary>
    internal void ShowRemaining(int remaining)
    {
        if (remaining == 1)
        {
            ShowOnlyState(lastSpinState);
            return;
        }

        if (remainingCountText != null)
        {
            string n = Mathf.Max(0, remaining).ToString();
            remainingCountText.text = countUsesSpriteDigits ? SpriteTextFormatter.ToSpriteDigits(n) : n;
        }
        ShowOnlyState(remainingState);
    }

    /// <summary>"FEATURE COMPLETE" (§5.6), instantly.</summary>
    internal void ShowFeatureComplete()
    {
        ShowOnlyState(featureCompleteState);
    }

    /// <summary>
    /// TOTAL PRIZE (§6.2): replaces FEATURE COMPLETE and pops in — from 0, overshooting, back to scale —
    /// its value blank until the first count-up.
    /// </summary>
    internal void PopInTotalPrize()
    {
        ShowOnlyState(null);
        totalPrizeShown = 0;
        if (totalPrizeValueText != null) totalPrizeValueText.text = string.Empty;
        if (totalPrizePanel == null) return;

        if (!totalPrizeHomeRead)
        {
            totalPrizeHomeRead = true;
            if (totalPrizePanel.localScale != Vector3.zero) totalPrizeHomeScale = totalPrizePanel.localScale;
        }

        totalPrizePanel.DOKill();
        totalPrizePanel.localScale = Vector3.zero;
        totalPrizePanel.gameObject.SetActive(true);
        tweens.Add(totalPrizePanel.DOScale(totalPrizeHomeScale, totalPrizePopDuration).SetEase(Ease.OutBack, totalPrizePopOvershoot));
    }

    /// <summary>
    /// TOTAL PRIZE counts up from what it shows to value, over duration — run while an orb opens (§6.3).
    /// </summary>
    internal void CountTotalPrizeTo(double value, float duration)
    {
        prizeCountTween?.Kill();

        if (duration <= 0f)
        {
            totalPrizeShown = value;
            WriteTotalPrize(value);
            return;
        }

        prizeCountTween = DOTween.To(() => totalPrizeShown, v => { totalPrizeShown = v; WriteTotalPrize(v); }, value, duration)
            .SetEase(Ease.Linear)
            .OnComplete(() =>
            {
                prizeCountTween = null;
                totalPrizeShown = value;
                WriteTotalPrize(value);
            });
    }

    #endregion

    #region Helpers

    // Exactly one of the top panel's states on (or none, for null).
    private void ShowOnlyState(GameObject state)
    {
        if (remainingState != null) remainingState.SetActive(remainingState == state);
        if (lastSpinState != null) lastSpinState.SetActive(lastSpinState == state);
        if (featureCompleteState != null) featureCompleteState.SetActive(featureCompleteState == state);
    }

    private void HideTotalPrize()
    {
        prizeCountTween?.Kill();
        prizeCountTween = null;
        totalPrizeShown = 0;
        if (totalPrizePanel != null)
        {
            totalPrizePanel.DOKill();
            if (totalPrizeHomeRead) totalPrizePanel.localScale = totalPrizeHomeScale;
            totalPrizePanel.gameObject.SetActive(false);
        }
        if (totalPrizeValueText != null) totalPrizeValueText.text = string.Empty;
    }

    private void WriteTotalPrize(double value)
    {
        if (totalPrizeValueText == null) return;
        totalPrizeValueText.text = prizeUsesSpriteDigits
            ? SpriteTextFormatter.ToSpriteMoney(value)
            : value.ToString(SpriteTextFormatter.MoneyFormat);
    }

    // Killed without completing, so an interrupted transition never calls back.
    private void KillTweens()
    {
        foreach (Tween tween in tweens) tween?.Kill();
        tweens.Clear();
        prizeCountTween?.Kill();
        prizeCountTween = null;
    }

    private void OnDestroy()
    {
        KillTweens();
    }

    #endregion
}
