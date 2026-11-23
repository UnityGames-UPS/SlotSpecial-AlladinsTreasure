using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// The free games' two popups (ATFreeGamesBonus.md §3.3, §5.5, §7):
///  - "N FREE GAMES" — the trigger's award and every retrigger's: pops in from 0 with an overshoot,
///    holds, then grows a little and shrinks to 0, on its own;
///  - "CONGRATULATIONS! YOU WIN X" — appears instantly, X counts up from 0 to the bonus total, and
///    COLLECT (greyed until the count-up ends) hands back to the controller. It has no exit of its
///    own: the transition covers it and the controller hides it behind that (§8).
///
/// View layer only: GameManager calls in and is called back. Every reference is optional — a missing
/// popup still calls back, so a round never stalls on it (an unwired COLLECT collects at once).
/// </summary>
public class FeaturePopupView : MonoBehaviour
{
    [Header("\"N FREE GAMES\"")]
    [Tooltip("The popup panel. Switched on to show; scaled from 0 in, and to 0 out.")]
    [SerializeField] private RectTransform awardedPopup;
    [Tooltip("N — the games added (10 on the trigger; 3, or fewer at the cap, on a retrigger).")]
    [SerializeField] private TMPro.TMP_Text awardedCountText;
    [Tooltip("The \"FREE GAMES\" label — shown for every N, 1 included (owner, 2026-11-16). Optional.")]
    [SerializeField] private GameObject awardedLabelPlural;
    [Tooltip("Seconds the popup holds before it goes on its own (owner: 5 s to start with).")]
    [SerializeField] private float awardedHold = 5f;
    [SerializeField] private float popInDuration = 0.4f;
    [SerializeField] private float popInOvershoot = 1.7f;
    [Tooltip("On the way out it first grows to this many times its size…")]
    [SerializeField] private float popOutGrowScale = 1.1f;
    [SerializeField] private float popOutGrowDuration = 0.12f;
    [Tooltip("…then shrinks to 0 over this.")]
    [SerializeField] private float popOutShrinkDuration = 0.25f;

    [Header("CONGRATULATIONS")]
    [Tooltip("The CONGRATULATIONS popup. Switched on as it is — no scale, no fade.")]
    [SerializeField] private GameObject congratulationsPopup;
    [Tooltip("X — counted up from 0 to the bonus total.")]
    [SerializeField] private TMPro.TMP_Text congratulationsAmountText;
    [SerializeField] private float congratulationsCountUpDuration = 2f;
    [Tooltip("COLLECT — greyed (not interactable) until the count-up ends. Its click is added in code.")]
    [SerializeField] private Button collectButton;

    private readonly List<Tween> tweens = new List<Tween>();
    private Vector3 awardedHomeScale = Vector3.one;
    private bool awardedHomeRead;
    private Action pendingCollect;

    private void Awake()
    {
        if (collectButton != null) collectButton.onClick.AddListener(OnCollectPressed);
    }

    #region Called by GameManager

    /// <summary>Both popups hidden, nothing running. At init, and behind the transition out (§8).</summary>
    internal void HideImmediate()
    {
        KillTweens();
        AudioManager.Instance?.StopTotalWin();
        pendingCollect = null;

        if (awardedPopup != null)
        {
            ReadAwardedHome();
            awardedPopup.localScale = awardedHomeScale;
            awardedPopup.gameObject.SetActive(false);
        }
        if (congratulationsPopup != null) congratulationsPopup.SetActive(false);
    }

    /// <summary>
    /// "N FREE GAMES" (§3.3): pops in, holds, pops out. onClosed fires once it has gone. N is the games
    /// actually added — never a constant.
    /// </summary>
    internal void ShowGamesAwarded(int games, Action onClosed)
    {
        if (awardedPopup == null)
        {
            onClosed?.Invoke();
            return;
        }

        KillTweens();

        if (awardedCountText != null)
        {
            // Sprite digits — AwardedCountText uses the AT-GoldText sprite asset.
            awardedCountText.text = SpriteTextFormatter.ToSpriteDigits(Mathf.Max(0, games).ToString());
        }
        if (awardedLabelPlural != null) awardedLabelPlural.SetActive(true);

        ReadAwardedHome();
        awardedPopup.DOKill();
        awardedPopup.localScale = Vector3.zero;
        awardedPopup.gameObject.SetActive(true);

        AudioManager.Instance?.PlayCongratulations();

        Sequence seq = DOTween.Sequence()
            .Append(awardedPopup.DOScale(awardedHomeScale, popInDuration).SetEase(Ease.OutBack, popInOvershoot))
            .AppendInterval(Mathf.Max(0f, awardedHold))
            .Append(awardedPopup.DOScale(awardedHomeScale * popOutGrowScale, popOutGrowDuration).SetEase(Ease.OutQuad))
            .Append(awardedPopup.DOScale(Vector3.zero, popOutShrinkDuration).SetEase(Ease.InBack))
            .OnComplete(() =>
            {
                awardedPopup.gameObject.SetActive(false);
                awardedPopup.localScale = awardedHomeScale;
                onClosed?.Invoke();
            });
        tweens.Add(seq);
    }

    /// <summary>
    /// CONGRATULATIONS! YOU WIN X (§7): shown instantly, X counting up from 0 to total; COLLECT goes live
    /// once the count has landed, and its press fires onCollect (once). The popup stays up — the
    /// controller hides it behind the transition.
    /// </summary>
    internal void ShowCongratulations(double total, Action onCollect)
    {
        KillTweens();
        pendingCollect = onCollect;

        if (congratulationsPopup == null)
        {
            Collect();
            return;
        }

        congratulationsPopup.SetActive(true);
        if (collectButton != null) collectButton.interactable = false;

        double shown = 0;
        WriteAmount(0);
        AudioManager.Instance?.PlayTotalWin();

        void Landed()
        {
            WriteAmount(total);
            if (collectButton != null) collectButton.interactable = true;
            else Collect();   // nothing to press: carry on rather than stall
        }

        if (congratulationsCountUpDuration <= 0f || total <= 0)
        {
            Landed();
            return;
        }

        tweens.Add(DOTween.To(() => shown, v => { shown = v; WriteAmount(v); }, total, congratulationsCountUpDuration)
            .SetEase(Ease.Linear)
            .OnComplete(Landed));
    }

    #endregion

    #region Helpers

    private void OnCollectPressed()
    {
        if (pendingCollect == null) return;
        if (collectButton != null) collectButton.interactable = false;
        AudioManager.Instance?.PlayTakeButton();
        Collect();
    }

    private void Collect()
    {
        Action callback = pendingCollect;
        pendingCollect = null;
        AudioManager.Instance?.StopTotalWin();
        callback?.Invoke();
    }

    private void WriteAmount(double amount)
    {
        if (congratulationsAmountText == null) return;
        // Sprite digits — CongratulationsAmountText uses the AT-GoldText sprite asset.
        congratulationsAmountText.text = SpriteTextFormatter.ToSpriteMoney(amount);
    }

    // The scale the popup was saved with, read once, so every pop lands back on it.
    private void ReadAwardedHome()
    {
        if (awardedHomeRead || awardedPopup == null) return;
        awardedHomeRead = true;
        if (awardedPopup.localScale != Vector3.zero) awardedHomeScale = awardedPopup.localScale;
    }

    // Killed without completing, so no callback of an interrupted popup ever fires.
    private void KillTweens()
    {
        foreach (Tween tween in tweens) tween?.Kill();
        tweens.Clear();
        if (awardedPopup != null) awardedPopup.DOKill();
    }

    private void OnDestroy()
    {
        KillTweens();
        if (collectButton != null) collectButton.onClick.RemoveListener(OnCollectPressed);
    }

    #endregion
}
