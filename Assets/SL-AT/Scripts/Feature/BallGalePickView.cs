using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// The Ball Gale Pick's own presentation (BallGalePickBonus.md §6, §7, §8): the slide from the reels
/// to the gem panel and back, the gems' glare, a pick's break and value, the running total and its
/// glow. The trigger boxes are SlotView's; the popups FeaturePopupView's.
///
/// View layer only: GameManager sequences the bonus and is called back when each step has finished.
/// It never calls into the game loop and never decides anything — which pick a click shows, and when
/// picking ends, are the server's. DOTween only, no coroutines, so it can sit on the panel it switches
/// on and off. Every reference is optional: a missing one skips its part and still calls back.
/// </summary>
public class BallGalePickView : MonoBehaviour
{
    [Header("Slide")]
    [Tooltip("SlotBG/SlotObject — the reels. Slides down off screen into the bonus, and back up after TAKE. Not SlotBG: the gem panel and the feature popup are under it too.")]
    [SerializeField] private RectTransform slotObject;

    [Tooltip("SlotBG/BallGalePick — the gem panel. Slides up into the reels' place. Empty: this object. Its position in the scene is where it rests.")]
    [SerializeField] private RectTransform ballGalePick;

    [Tooltip("The logo above the reels (Logo) — faded out on the way in, back in on the way out. The same one FreeGameView fades; the two never run together.")]
    [SerializeField] private CanvasGroup logoGroup;

    [Tooltip("FreeGamesRemainingText — the FREE GAME counter, faded instead of the logo when the bonus is won inside Free Spins (the logo is off then). Empty: the header is left as it is during Free Spins.")]
    [SerializeField] private CanvasGroup freeGamesCounterGroup;

    [Header("Gems")]
    [Tooltip("The 20 gems in reading order: the top row left to right, then the rows below. Filled by the component's \"Fill gems from children\" menu.")]
    [SerializeField] private List<GaleGem> gems = new List<GaleGem>(20);

    [Tooltip("The glare, played once on a random group of unpicked gems at a time. Handed to the gems at runtime — their own frame lists stay empty. The first frame is the gem at rest.")]
    [SerializeField] private List<Sprite> glareFrames = new List<Sprite>();

    [Tooltip("The break, played once on the picked gem. Its value shows once it has finished.")]
    [SerializeField] private List<Sprite> breakFrames = new List<Sprite>();

    [Header("Multiplier box")]
    [Tooltip("The running total (TotalMultiplierValue) — empty until the first value. Sprite digits, the \"x\" as sprite 11 — its sprite asset is set in the scene.")]
    [SerializeField] private TMPro.TMP_Text totalText;

    [Tooltip("The glow behind the total, looping from the end of the picking until TAKE — its frames go on its own ImageAnimation. Optional: skipped while unwired or without frames.")]
    [SerializeField] private ImageAnimation glow;

    // Tuning — placeholders until the owner signs off the timings (BallGalePickBonus.md §11).
    private const float SlideDuration = 0.6f;        // the reels down / the panel up, and back
    private const float LogoFadeDuration = 0.4f;     // the logo out, and back in
    private const float GlareInterval = 1.5f;        // between one glare group and the next
    private const int GlareGroupMin = 3;             // gems in a glare group, at least ...
    private const int GlareGroupMax = 5;             // ... and at most
    private const float RevealSettle = 0.3f;         // a shown value stands this long before the next pick opens
    private const float FallbackSlideDistance = 1080f;   // used only if no canvas is found above the panel

    // Where the reels and the panel rest, read from the scene on first use.
    private bool homesCaptured;
    private Vector2 slotObjectHome;
    private Vector2 ballGalePickHome;

    // Each gem's state for the bonus in progress, and its resting sprite from the scene.
    private bool[] gemPicked = new bool[0];
    private bool[] gemGlaring = new bool[0];
    private Sprite[] gemRestSprites;

    private bool pickOpen;
    private Action<int> onGemClicked;

    private Sequence slideSequence;
    private Sequence glareLoop;
    private Tween revealDelay;

    private RectTransform BallGalePick => ballGalePick != null ? ballGalePick : transform as RectTransform;

    // No Start / Awake work on purpose: this object is switched off between bonuses, and a deferred
    // Start could run mid-bonus. GameManager calls ResetToBaseGame at init instead.

    #region Called by GameManager

    /// <summary>
    /// The base game's look: the reels on and in place, the panel off, every gem whole, the logo on.
    /// Called at init, so the scene's saved state never shows.
    /// </summary>
    internal void ResetToBaseGame()
    {
        CaptureHomes();
        KillTweens();
        StopGlow();
        PrepareGems();

        if (slotObject != null)
        {
            slotObject.anchoredPosition = slotObjectHome;
            slotObject.gameObject.SetActive(true);
        }

        RectTransform ballGalePickRect = BallGalePick;
        if (ballGalePickRect != null)
        {
            ballGalePickRect.anchoredPosition = ballGalePickHome;
            ballGalePickRect.gameObject.SetActive(false);
        }

        if (logoGroup != null)
        {
            logoGroup.DOKill();
            logoGroup.alpha = 1f;
            logoGroup.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Into the bonus (BallGalePickBonus.md §6.1): every gem whole and locked, the total empty; the
    /// reels slide down off screen, the header fades out — the logo, or inside Free Spins the FREE GAME
    /// counter — the panel slides up into the reels' place. The glare starts once it is there; onDone
    /// fires then.
    /// </summary>
    internal void EnterBonus(Action onDone, bool inFreeSpins)
    {
        CaptureHomes();
        KillTweens();
        StopGlow();
        PrepareGems();

        float distance = SlideDistance();
        Vector2 down = new Vector2(0f, distance);
        CanvasGroup header = HeaderGroup(inFreeSpins);

        RectTransform ballGalePickRect = BallGalePick;
        if (ballGalePickRect != null)
        {
            ballGalePickRect.anchoredPosition = ballGalePickHome - down;
            ballGalePickRect.gameObject.SetActive(false);
        }

        AudioManager.Instance?.PlayBonusSwoosh();
        slideSequence = DOTween.Sequence();

        if (slotObject != null)
        {
            slotObject.anchoredPosition = slotObjectHome;
            slideSequence.Append(slotObject.DOAnchorPos(slotObjectHome - down, SlideDuration).SetEase(Ease.InCubic));
        }

        if (header != null)
        {
            slideSequence.Append(header.DOFade(0f, LogoFadeDuration).SetEase(Ease.Linear));
            slideSequence.AppendCallback(() => header.gameObject.SetActive(false));
        }

        if (ballGalePickRect != null)
        {
            slideSequence.AppendCallback(() => ballGalePickRect.gameObject.SetActive(true));
            slideSequence.Append(ballGalePickRect.DOAnchorPos(ballGalePickHome, SlideDuration).SetEase(Ease.OutCubic));
        }

        // Keeps the step a beat even with nothing wired, so the caller's wait always ends.
        slideSequence.AppendInterval(0f);
        slideSequence.OnComplete(() =>
        {
            slideSequence = null;
            StartGlare();
            onDone?.Invoke();
        });
    }

    /// <summary>
    /// The unpicked gems take one click. onGemClicked fires once, with the gem's index; every gem is
    /// locked again from that moment. With no gem left to click it fires at once with -1, so the bonus
    /// never waits on a click that can't come.
    /// </summary>
    internal void OpenPicking(Action<int> onGemClickedCallback)
    {
        onGemClicked = onGemClickedCallback;
        pickOpen = true;

        bool anyClickable = false;
        for (int i = 0; i < gems.Count; i++)
        {
            GaleGem gem = gems[i];
            if (gem == null || gem.button == null || IsPicked(i)) continue;

            gem.button.interactable = true;
            anyClickable = true;
        }

        if (!anyClickable)
        {
            Debug.LogWarning("[BallGalePickView] No unpicked gem is wired to click — revealing the pick without one.");
            OnGemClicked(-1);
        }
    }

    /// <summary>
    /// One pick (BallGalePickBonus.md §6.4): its value ("X" + value, or COLLECT) goes up on the gem's
    /// border (GemBorders, drawn behind the gems, always on) and the gem breaks over it; once the break
    /// has played, the gem's image goes and the box shows the running total. onRevealed fires a moment after.
    /// gemIndex -1 (nothing clicked) shows only the total.
    /// </summary>
    internal void RevealGem(int gemIndex, BallPick pick, double total, Action onRevealed)
    {
        GaleGem gem = GetGem(gemIndex);
        if (gem != null)
        {
            gemPicked[gemIndex] = true;
            AudioManager.Instance?.PlayGemBreak();
        }

        // The value (or COLLECT) goes up on the border before the break starts (owner, 2026-11-09), so
        // the breaking gem uncovers it. The box's running total still waits for the break to end.
        if (gem != null) ShowGemValue(gem, pick);

        void ShowResult()
        {
            if (pick != null && !pick.isCollect && totalText != null) totalText.text = SpriteTextFormatter.ToSpriteMultiplier(total);

            revealDelay?.Kill();
            revealDelay = DOVirtual.DelayedCall(RevealSettle, () =>
            {
                revealDelay = null;
                onRevealed?.Invoke();
            });
        }

        if (gem == null || gem.animation == null || breakFrames == null || breakFrames.Count == 0)
        {
            if (gem != null && gem.image != null) gem.image.enabled = false;
            ShowResult();
            return;
        }

        ImageAnimation animation = gem.animation;
        StopGemAnimation(gemIndex);

        animation.textureArray = breakFrames;
        animation.doLoopAnimation = false;
        // Speed = frame count plays it at 24 fps (ImageAnimation: frames² ÷ (24 × speed) a loop).
        animation.AnimationSpeed = breakFrames.Count;
        animation.onLoopComplete = _ =>
        {
            animation.onLoopComplete = null;
            // The Image, not the object: the button is its child. The border behind, with the value on
            // it, shows through.
            if (gem.image != null) gem.image.enabled = false;
            animation.StopAnimation();
            ShowResult();
        };
        animation.StartAnimation();
    }

    /// <summary>
    /// Picking is over (BallGalePickBonus.md §6.5): the glare stops and the glow behind the total
    /// starts, looping until ExitBonus.
    /// </summary>
    internal void EndPicking()
    {
        StopGlare();
        LockAllGems();

        if (glow == null || glow.textureArray == null || glow.textureArray.Count == 0) return;

        glow.doLoopAnimation = true;
        // Speed = frame count plays it at 24 fps (ImageAnimation: frames² ÷ (24 × speed) a loop).
        glow.AnimationSpeed = glow.textureArray.Count;
        glow.gameObject.SetActive(true);
        glow.StartAnimation();
    }

    /// <summary>
    /// Out of the bonus, after TAKE (BallGalePickBonus.md §7): the panel slides down and goes off,
    /// the header fades in — the logo, or inside Free Spins the FREE GAME counter — the reels slide back
    /// up. onDone fires once they are in place.
    /// </summary>
    internal void ExitBonus(Action onDone, bool inFreeSpins)
    {
        CaptureHomes();
        KillTweens();
        StopGlow();
        LockAllGems();

        float distance = SlideDistance();
        Vector2 down = new Vector2(0f, distance);
        RectTransform ballGalePickRect = BallGalePick;
        CanvasGroup header = HeaderGroup(inFreeSpins);

        AudioManager.Instance?.PlayBonusSwoosh();
        slideSequence = DOTween.Sequence();

        if (ballGalePickRect != null && ballGalePickRect.gameObject.activeSelf)
        {
            slideSequence.Append(ballGalePickRect.DOAnchorPos(ballGalePickHome - down, SlideDuration).SetEase(Ease.InCubic));
        }
        if (ballGalePickRect != null)
        {
            slideSequence.AppendCallback(() =>
            {
                ballGalePickRect.gameObject.SetActive(false);
                ballGalePickRect.anchoredPosition = ballGalePickHome;
            });
        }

        if (header != null)
        {
            slideSequence.AppendCallback(() =>
            {
                header.alpha = 0f;
                header.gameObject.SetActive(true);
            });
            slideSequence.Append(header.DOFade(1f, LogoFadeDuration).SetEase(Ease.Linear));
        }

        if (slotObject != null)
        {
            slideSequence.AppendCallback(() => slotObject.gameObject.SetActive(true));
            slideSequence.Append(slotObject.DOAnchorPos(slotObjectHome, SlideDuration).SetEase(Ease.OutCubic));
        }

        slideSequence.AppendInterval(0f);
        slideSequence.OnComplete(() =>
        {
            slideSequence = null;
            onDone?.Invoke();
        });
    }

    #endregion

    #region Helpers

    // The player's click. Taken once: every gem locks, and GameManager is told which.
    private void OnGemClicked(int gemIndex)
    {
        if (!pickOpen) return;
        pickOpen = false;

        LockAllGems();

        var callback = onGemClicked;
        onGemClicked = null;
        callback?.Invoke(gemIndex);
    }

    // Every gem whole and at rest (covering its border), its value hidden, its button locked and
    // listening; the total empty. The start of every bonus, and init.
    private void PrepareGems()
    {
        int count = gems != null ? gems.Count : 0;
        if (gemPicked.Length != count)
        {
            gemPicked = new bool[count];
            gemGlaring = new bool[count];
        }

        pickOpen = false;
        onGemClicked = null;

        for (int i = 0; i < count; i++)
        {
            gemPicked[i] = false;
            gemGlaring[i] = false;

            GaleGem gem = gems[i];
            if (gem == null) continue;

            if (gem.animation != null)
            {
                gem.animation.onLoopComplete = null;
                gem.animation.StopAnimation();
                if (glareFrames != null && glareFrames.Count > 0) gem.animation.textureArray = glareFrames;
            }

            if (gem.image != null)
            {
                if (gemRestSprites != null && i < gemRestSprites.Length && gemRestSprites[i] != null)
                {
                    gem.image.sprite = gemRestSprites[i];
                }
                gem.image.enabled = true;
            }

            if (gem.valueText != null) gem.valueText.gameObject.SetActive(false);
            if (gem.collectImage != null) gem.collectImage.SetActive(false);

            if (gem.button != null)
            {
                int index = i;
                gem.button.onClick.RemoveAllListeners();
                gem.button.onClick.AddListener(() => OnGemClicked(index));
                gem.button.interactable = false;
            }
        }

        if (totalText != null) totalText.text = string.Empty;
    }

    // What the broken gem uncovers on its border: the COLLECT image, or the value in sprite digits ("x"
    // as sprite 11, from GemValueText's sprite asset). With no COLLECT image wired, COLLECT is written.
    private void ShowGemValue(GaleGem gem, BallPick pick)
    {
        if (pick != null && pick.isCollect && gem.collectImage != null)
        {
            gem.collectImage.SetActive(true);
            return;
        }

        if (gem.valueText == null) return;

        gem.valueText.text = pick == null ? string.Empty : (pick.isCollect ? "COLLECT" : SpriteTextFormatter.ToSpriteMultiplier(pick.value));
        gem.valueText.gameObject.SetActive(true);
    }

    private void LockAllGems()
    {
        pickOpen = false;
        if (gems == null) return;

        foreach (GaleGem gem in gems)
        {
            if (gem != null && gem.button != null) gem.button.interactable = false;
        }
    }

    #endregion

    #region Glare

    // One shared timer: every GlareInterval a random group of unpicked, idle gems glares together,
    // all started in the same frame so they stay in sync (BallGalePickBonus.md §6.3).
    private void StartGlare()
    {
        StopGlare();
        if (glareFrames == null || glareFrames.Count == 0) return;

        glareLoop = DOTween.Sequence()
            .AppendInterval(GlareInterval)
            .AppendCallback(PlayGlareGroup)
            .SetLoops(-1);
    }

    private void PlayGlareGroup()
    {
        var candidates = new List<int>();
        for (int i = 0; i < gems.Count; i++)
        {
            GaleGem gem = gems[i];
            if (gem == null || gem.animation == null || IsPicked(i) || gemGlaring[i]) continue;
            candidates.Add(i);
        }
        if (candidates.Count == 0) return;

        int groupSize = Mathf.Min(candidates.Count, UnityEngine.Random.Range(GlareGroupMin, GlareGroupMax + 1));

        // A partial shuffle: the first groupSize entries become a random pick.
        for (int i = 0; i < groupSize; i++)
        {
            int swap = UnityEngine.Random.Range(i, candidates.Count);
            (candidates[i], candidates[swap]) = (candidates[swap], candidates[i]);
        }

        for (int i = 0; i < groupSize; i++)
        {
            int index = candidates[i];
            ImageAnimation animation = gems[index].animation;

            gemGlaring[index] = true;
            animation.textureArray = glareFrames;
            animation.doLoopAnimation = false;
            animation.AnimationSpeed = glareFrames.Count;
            animation.onLoopComplete = _ =>
            {
                animation.onLoopComplete = null;
                gemGlaring[index] = false;
                // Back to the first frame: the gem at rest.
                animation.StopAnimation();
            };
            animation.StartAnimation();
        }
    }

    private void StopGlare()
    {
        glareLoop?.Kill();
        glareLoop = null;

        if (gems == null) return;
        for (int i = 0; i < gems.Count; i++)
        {
            if (i < gemGlaring.Length && gemGlaring[i]) StopGemAnimation(i);
        }
    }

    // Stops whatever a gem is playing, back on its rest frame.
    private void StopGemAnimation(int index)
    {
        GaleGem gem = GetGem(index);
        if (gem == null || gem.animation == null) return;

        gem.animation.onLoopComplete = null;
        gem.animation.StopAnimation();
        if (index < gemGlaring.Length) gemGlaring[index] = false;
    }

    private void StopGlow()
    {
        if (glow == null) return;

        glow.StopAnimation();
        glow.gameObject.SetActive(false);
    }

    #endregion

    #region Utilities

    // The rest positions and sprites, as the scene was saved — read once.
    private void CaptureHomes()
    {
        if (homesCaptured) return;
        homesCaptured = true;

        if (slotObject != null) slotObjectHome = slotObject.anchoredPosition;
        RectTransform ballGalePickRect = BallGalePick;
        if (ballGalePickRect != null) ballGalePickHome = ballGalePickRect.anchoredPosition;

        int count = gems != null ? gems.Count : 0;
        gemRestSprites = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            if (gems[i] != null && gems[i].image != null) gemRestSprites[i] = gems[i].image.sprite;
        }
    }

    // What fades above the reels: the logo in the base game; inside Free Spins the logo is off and the
    // FREE GAME counter stands in its place, so that is faded instead. Null: no header fade.
    private CanvasGroup HeaderGroup(bool inFreeSpins)
    {
        return inFreeSpins ? freeGamesCounterGroup : logoGroup;
    }

    // How far the reels and the panel move: the canvas's height, which takes either fully off screen.
    private float SlideDistance()
    {
        Canvas canvas = GetComponentInParent<Canvas>(true);
        if (canvas != null && canvas.rootCanvas != null)
        {
            float height = ((RectTransform)canvas.rootCanvas.transform).rect.height;
            if (height > 0f) return height;
        }
        return FallbackSlideDistance;
    }

    private GaleGem GetGem(int index)
    {
        if (gems == null || index < 0 || index >= gems.Count) return null;
        return gems[index];
    }

    private bool IsPicked(int index)
    {
        return index >= 0 && index < gemPicked.Length && gemPicked[index];
    }

    private void KillTweens()
    {
        slideSequence?.Kill();
        slideSequence = null;
        revealDelay?.Kill();
        revealDelay = null;
        StopGlare();
        if (logoGroup != null) logoGroup.DOKill();
        if (freeGamesCounterGroup != null) freeGamesCounterGroup.DOKill();
        if (slotObject != null) slotObject.DOKill();
        if (BallGalePick != null) BallGalePick.DOKill();
    }

    private void OnDestroy()
    {
        slideSequence?.Kill();
        glareLoop?.Kill();
        revealDelay?.Kill();
    }

    #endregion

#if UNITY_EDITOR
    #region Editor — fill the gems

    // How far apart (in the panel's units) a gem and its border may sit and still be paired.
    private const float BorderMatchTolerance = 5f;

    /// <summary>
    /// Editor only (right-click the component → "Fill gems from children"): fills gems from the
    /// hierarchy under this object, so the references needn't be dragged by hand. Reads the structure,
    /// not names:
    ///  - a gem is any object with an ImageAnimation and a Button somewhere inside it, taken in
    ///    hierarchy order (the top row left to right, then the rows below): its own Image and
    ///    ImageAnimation, and the Button inside it;
    ///  - a border (GemBorders) is any object holding a TMP text as a direct child and no Button: that
    ///    text is the gem's value, and its Image child, if any, the COLLECT image;
    ///  - each gem takes the border in the same place on the panel — by position, not order, so a
    ///    border in the wrong row is reported rather than silently paired with the wrong gem.
    /// Undoable; save the scene afterwards. Anything missing is logged.
    /// </summary>
    [ContextMenu("Fill gems from children")]
    private void FillGemsFromChildren()
    {
        UnityEditor.Undo.RecordObject(this, "Fill gems from children");

        var filled = new List<GaleGem>();
        int problems = 0;

        var borders = FindBorders();
        var usedBorders = new HashSet<Transform>();

        foreach (ImageAnimation animation in GetComponentsInChildren<ImageAnimation>(true))
        {
            Transform gemTransform = animation.transform;
            Button button = gemTransform.GetComponentInChildren<Button>(true);
            if (button == null) continue;   // not a gem — the glow, or a border's leftover component

            var gem = new GaleGem
            {
                image = gemTransform.GetComponent<Image>(),
                animation = animation,
                button = button
            };

            string where = $"{gemTransform.parent?.name} / {gemTransform.name}";
            if (gem.image == null) { problems++; Debug.LogWarning($"[BallGalePickView] {where}: no Image on the gem.", gemTransform); }

            Transform border = NearestBorder(gemTransform, borders, usedBorders);
            if (border == null)
            {
                problems++;
                Debug.LogWarning($"[BallGalePickView] {where}: no border found in the same place — no value text or COLLECT image for this gem.", gemTransform);
            }
            else
            {
                usedBorders.Add(border);
                gem.valueText = DirectChildText(border);

                // The COLLECT image: the border's first child with an Image (the text has none).
                foreach (Transform child in border)
                {
                    if (child.GetComponent<Image>() == null || child.GetComponent<Button>() != null) continue;
                    gem.collectImage = child.gameObject;
                    break;
                }
                if (gem.collectImage == null) Debug.LogWarning($"[BallGalePickView] {where}: its border {border.parent?.name} / {border.name} has no COLLECT image — COLLECT will be written as text.", border);
            }

            filled.Add(gem);
        }

        foreach (Transform border in borders)
        {
            if (!usedBorders.Contains(border)) Debug.LogWarning($"[BallGalePickView] Border {border.parent?.name} / {border.name} matched no gem.", border);
        }

        gems = filled;
        UnityEditor.EditorUtility.SetDirty(this);

        if (filled.Count != 20) Debug.LogWarning($"[BallGalePickView] Found {filled.Count} gems — expected 20. Does anything else under the panel carry an ImageAnimation and a Button?", this);
        Debug.Log($"[BallGalePickView] Filled {filled.Count} gems, {usedBorders.Count} with their border" +
                  (problems > 0 ? $" — {problems} problems, see the warnings." : " — save the scene to keep them."), this);
    }

    // Every object under the panel holding a TMP text as a direct child and no Button — the borders.
    // The multiplier box's total is left out.
    private List<Transform> FindBorders()
    {
        var found = new List<Transform>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t == transform) continue;
            TMPro.TMP_Text text = DirectChildText(t);
            if (text == null || text == totalText) continue;
            if (t.GetComponentInChildren<Button>(true) != null) continue;
            found.Add(t);
        }
        return found;
    }

    private static TMPro.TMP_Text DirectChildText(Transform parent)
    {
        foreach (Transform child in parent)
        {
            TMPro.TMP_Text text = child.GetComponent<TMPro.TMP_Text>();
            if (text != null) return text;
        }
        return null;
    }

    // The unused border closest to the gem, if it is within BorderMatchTolerance of it.
    private Transform NearestBorder(Transform gemTransform, List<Transform> borders, HashSet<Transform> used)
    {
        Vector3 gemPosition = transform.InverseTransformPoint(gemTransform.position);
        Transform best = null;
        float bestDistance = BorderMatchTolerance;

        foreach (Transform border in borders)
        {
            if (used.Contains(border)) continue;
            float distance = Vector2.Distance(gemPosition, transform.InverseTransformPoint(border.position));
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = border;
        }
        return best;
    }

    #endregion
#endif
}

// One gem on the bonus panel (BallGalePick/.../GemRow/Gem) and what it reveals, which sits on its
// border (GemBorders/BorderRow/Border), behind it. Wired explicitly, like the pick cells.
[Serializable]
public class GaleGem
{
    public Image image;                 // the gem — disabled (not deactivated) once it has broken
    public ImageAnimation animation;    // on the gem: the glare, then the break
    public Button button;               // GaleButton — the click target, inside the gem
    public TMPro.TMP_Text valueText;    // the border's GemValueText — "X" + value in sprite digits
    public GameObject collectImage;     // the border's CollectImage — shown instead of the value on COLLECT
}
