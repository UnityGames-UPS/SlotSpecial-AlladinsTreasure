using System;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// Free Spins' screen furniture (BonusFreeSpins.md §5, §7, §8): the fade to black on the way in,
/// the "FREE GAME X OF Y" panel that takes the logo's place for the round, and the panel / logo
/// crossfade on the way out. The popups themselves are FeaturePopupView's.
///
/// View layer only. GameManager drives this and is called back when a fade has finished; this never
/// calls into the game loop. DOTween-driven, so nothing depends on which objects are switched on.
/// Every reference is optional — a missing one skips its part and still calls back.
/// </summary>
public class FreeGameView : MonoBehaviour
{
    [Header("Free Game panel")]
    [Tooltip("The \"FREE GAME X OF Y\" panel above the reels (FreeGamesRemainingText). Its CanvasGroup is used for the fade out at TAKE. The \"FREE GAME\" and \"OF\" images are part of it — only the two numbers are written.")]
    [SerializeField] private GameObject counterPanel;

    [Tooltip("X — the spins played so far, this one included (RemainingFreeSpins in the scene). Sprite digits.")]
    [SerializeField] private TMPro.TMP_Text playedSpinsText;

    [Tooltip("Y — the spins awarded in the round, retriggers included. Sprite digits.")]
    [SerializeField] private TMPro.TMP_Text totalSpinsText;

    [Header("Logo and black film")]
    [Tooltip("The logo above the reels (Logo). Switched off while the screen is black on the way in, faded back in at TAKE.")]
    [SerializeField] private CanvasGroup logoGroup;

    [Tooltip("The full-screen black film (BlackFlim), faded up and down on the way into the round.")]
    [SerializeField] private CanvasGroup blackFilm;

    // Tuning — placeholders until the owner signs off the timings (BonusFreeSpins.md §11).
    private const float BlackFadeDuration = 0.5f;            // each way: to black, and back
    private const float BlackHold = 0.2f;                    // fully black, while the panel swaps in
    private const float PanelLogoCrossfadeDuration = 0.6f;   // panel out / logo in, together, at TAKE

    private Sequence activeSequence;

    #region Called by GameManager

    /// <summary>The base game's look: logo on, panel and black film off. Called at init.</summary>
    internal void ResetToBaseGame()
    {
        KillSequence();

        SetGroup(logoGroup, true, 1f);
        SetPanel(false, 1f);
        SetGroup(blackFilm, false, 0f);
    }

    /// <summary>
    /// Into the round: the screen fades to black; while black, the logo goes and the panel comes with
    /// the counter written; the screen fades back in. onBlack fires while the screen is fully black —
    /// the moment for anything else to change unseen. onDone fires once it is clear again.
    /// </summary>
    internal void EnterRound(int played, int total, Action onBlack, Action onDone)
    {
        KillSequence();
        WriteCounter(played, total);

        void SwapToPanel()
        {
            SetGroup(logoGroup, false, 1f);
            SetPanel(true, 1f);
            onBlack?.Invoke();
        }

        if (blackFilm == null)
        {
            SwapToPanel();
            onDone?.Invoke();
            return;
        }

        SetGroup(blackFilm, true, 0f);

        activeSequence = DOTween.Sequence()
            .Append(blackFilm.DOFade(1f, BlackFadeDuration).SetEase(Ease.Linear))
            .AppendCallback(SwapToPanel)
            .AppendInterval(BlackHold)
            .Append(blackFilm.DOFade(0f, BlackFadeDuration).SetEase(Ease.Linear))
            .OnComplete(() =>
            {
                activeSequence = null;
                SetGroup(blackFilm, false, 0f);
                onDone?.Invoke();
            });
    }

    /// <summary>
    /// X and Y. Counts, not money — ToSpriteDigits, so 10 stays "10" and not "10.00".
    /// </summary>
    internal void UpdateCounter(int played, int total)
    {
        WriteCounter(played, total);
    }

    /// <summary>
    /// Out of the round, at TAKE: the panel fades out while the logo fades in, together. onDone fires
    /// once both have finished.
    /// </summary>
    internal void ExitRound(Action onDone)
    {
        KillSequence();

        CanvasGroup panelGroup = counterPanel != null ? counterPanel.GetComponent<CanvasGroup>() : null;

        SetGroup(logoGroup, true, 0f);

        activeSequence = DOTween.Sequence();
        if (logoGroup != null) activeSequence.Join(logoGroup.DOFade(1f, PanelLogoCrossfadeDuration).SetEase(Ease.Linear));
        if (panelGroup != null) activeSequence.Join(panelGroup.DOFade(0f, PanelLogoCrossfadeDuration).SetEase(Ease.Linear));
        // Keeps the length even when neither group is wired, so the step still reads as a beat.
        activeSequence.AppendInterval(0f);

        activeSequence.OnComplete(() =>
        {
            activeSequence = null;
            SetPanel(false, 1f);
            SetGroup(logoGroup, true, 1f);
            onDone?.Invoke();
        });
    }

    #endregion

    #region Helpers

    private void WriteCounter(int played, int total)
    {
        if (playedSpinsText != null) playedSpinsText.text = SpriteTextFormatter.ToSpriteDigits(Mathf.Max(0, played).ToString());
        if (totalSpinsText != null) totalSpinsText.text = SpriteTextFormatter.ToSpriteDigits(Mathf.Max(0, total).ToString());
    }

    // The panel on or off, its CanvasGroup (if it has one) at the given alpha.
    private void SetPanel(bool on, float alpha)
    {
        if (counterPanel == null) return;

        CanvasGroup panelGroup = counterPanel.GetComponent<CanvasGroup>();
        if (panelGroup != null)
        {
            panelGroup.DOKill();
            panelGroup.alpha = alpha;
        }
        counterPanel.SetActive(on);
    }

    private static void SetGroup(CanvasGroup group, bool on, float alpha)
    {
        if (group == null) return;

        group.DOKill();
        group.alpha = alpha;
        group.gameObject.SetActive(on);
    }

    // Killed without completing, so an interrupted fade never calls back.
    private void KillSequence()
    {
        activeSequence?.Kill();
        activeSequence = null;
    }

    private void OnDestroy()
    {
        KillSequence();
    }

    #endregion
}
