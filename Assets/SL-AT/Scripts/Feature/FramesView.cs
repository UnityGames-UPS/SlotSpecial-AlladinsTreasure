using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Spine.Unity;

/// <summary>
/// The free games' layer over the board (ATFreeGamesBonus.md §5.3, §6): one cell per position, 5 reels
/// x 3 rows, each holding a frame, the effect played on top of it, the end orb and its prize number.
///
/// A frame is a still image, never animated: it is switched on (Blue) or its sprite swapped (Purple,
/// Orange) instantly, and the one effect clip plays once on top of it as it changes. An Orange hit
/// plays no effect: its BlueOrb plays once as the Blue orb, fades, and leaves its amount on the cell
/// until the next spin (RevealHits). At the end every
/// frame turns into an end orb that pops in, then — one at a time — plays its animation once, fades
/// away, and its prize number appears in its place.
///
/// The orb's animation: its jewel Spine (Blue / Magenta / Yellow — handed in by GameManager from
/// SlotView) when the cell has a Spine object, else its still image held for orbRevealFallback. The
/// orbs have no frame-clip path (owner removed it, 2026-11-20).
///
/// This object sits ABOVE SlotView's AnimationLayer in the hierarchy, so the frames draw over the
/// winning-symbol animations (owner, 2026-11-13).
///
/// View layer only: GameManager calls in and is called back. Every reference is optional — a missing
/// cell, clip or sprite is skipped and the callback still fires, so a round never stalls here.
/// </summary>
public class FramesView : MonoBehaviour
{
    [Header("Cells")]
    [Tooltip("One entry per reel, left to right; each holds its 3 cells top to bottom. A cell: Frame (Image), Effect (Image + ImageAnimation, above the frame), Orb (Image, with a Spine child), Prize (TMP text, above the orb).")]
    [SerializeField] private List<FrameColumn> frameColumns = new List<FrameColumn>(5);

    [Header("Frames")]
    [SerializeField] private Sprite frameBlue;
    [SerializeField] private Sprite framePurple;
    [SerializeField] private Sprite frameOrange;
    [Tooltip("The effect played once on top of a frame whenever it appears or upgrades — the same clip for every colour. Handed to each cell's Effect at runtime; their own frame lists stay empty.")]
    [SerializeField] private List<Sprite> frameEffectFrames = new List<Sprite>();
    [Tooltip("The effect's ImageAnimation speed. One pass lasts frames² ÷ (24 × speed) seconds.")]
    [SerializeField] private float frameEffectSpeed = 20f;

    [Header("End orbs")]
    [Tooltip("The Blue orb's resting sprite — the BlueOrb symbol's art (owner, 2026-11-13).")]
    [SerializeField] private Sprite orbBlue;
    [SerializeField] private Sprite orbPurple;
    [Tooltip("The Yellow orb — what an Orange frame becomes.")]
    [SerializeField] private Sprite orbYellow;
    [Tooltip("How long an orb's still image is held before it fades, when it has no Spine (a cell without a Spine object, or no jewel Spine handed in).")]
    [SerializeField] private float orbRevealFallback = 1f;
    [Tooltip("The orb fading away once its animation has played, before its prize appears.")]
    [SerializeField] private float orbFadeDuration = 0.3f;
    [Tooltip("How long each prize number holds after it appears — TOTAL PRIZE counts up over it — before the next orb starts.")]
    [SerializeField] private float prizeHold = 1f;
    [Tooltip("The orbs' pop in: from 0, overshooting, back to their scene scale.")]
    [SerializeField] private float orbPopDuration = 0.4f;
    [SerializeField] private float orbPopOvershoot = 1.7f;
    [Tooltip("The prize number scaling in at the orb's centre.")]
    [SerializeField] private float prizeScaleInDuration = 0.3f;

    // The board's reel count, for turning a flat cell index (row * reelCount + col) into a cell.
    private int reelCount = 5;

    private readonly List<Tween> tweens = new List<Tween>();

    // The end orbs' jewel Spines, by level (1 Blue, 2 Magenta, 3 Yellow). Handed in by GameManager from
    // SlotView, which owns the Spine assets (the Blue one is the BlueOrb symbol's) — not wired here.
    private SkeletonDataAsset orbSpineBlue;
    private SkeletonDataAsset orbSpinePurple;
    private SkeletonDataAsset orbSpineYellow;

    #region Called by GameManager

    /// <summary>The board's reel count, from the init. Flat cell indices are row * reelCount + col.</summary>
    internal void Configure(int boardReelCount)
    {
        if (boardReelCount > 0) reelCount = boardReelCount;
    }

    /// <summary>The end orbs' jewel Spines. Any left null falls back to that orb's still image.</summary>
    internal void SetOrbSpines(SkeletonDataAsset blue, SkeletonDataAsset purple, SkeletonDataAsset yellow)
    {
        orbSpineBlue = blue;
        orbSpinePurple = purple;
        orbSpineYellow = yellow;
    }

    /// <summary>How long a prize number holds after it appears; GameManager counts TOTAL PRIZE up over it.</summary>
    internal float PrizeHold => Mathf.Max(0f, prizeHold);

    /// <summary>Everything off and still: no frames, no orbs, no numbers. The base game's state.</summary>
    internal void ResetAll()
    {
        KillTweens();

        foreach (FrameCell cell in AllCells())
        {
            if (cell.frame != null) cell.frame.gameObject.SetActive(false);
            StopAndHide(cell.effect);
            HideOrb(cell);
            if (cell.prize != null) cell.prize.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// The trigger's frames (§4), after the transition: every position with a level gets its frame
    /// switched on, each with the effect on top, all together. onDone fires once every effect has played.
    /// levels is column-major ([reel][row]), as FreeGamesData.frameLevels.
    /// </summary>
    internal void ShowFrames(List<List<int>> levels, Action onDone)
    {
        var cells = new List<int>();
        var cellLevels = new List<int>();

        if (levels != null)
        {
            for (int col = 0; col < levels.Count; col++)
            {
                if (levels[col] == null) continue;
                for (int row = 0; row < levels[col].Count; row++)
                {
                    if (levels[col][row] <= 0) continue;
                    cells.Add(row * reelCount + col);
                    cellLevels.Add(levels[col][row]);
                }
            }
        }

        PlayFrameChanges(cells, cellLevels, onDone);
    }

    /// <summary>
    /// One free game's frame changes (§5.3), all at the same moment: each frame swapped to its new
    /// colour (or switched on, for a new Blue), with the effect on top. onDone fires once every effect
    /// has played — straight away when there is nothing to show.
    ///
    /// An Orange hit (Orange stays Orange, 10x total bet) changes no frame, so it plays no effect here
    /// (owner, 2026-11-23) — it goes through RevealHits instead.
    /// </summary>
    internal void ApplyChanges(List<FrameChange> changes, Action onDone)
    {
        var cells = new List<int>();
        var cellLevels = new List<int>();

        if (changes != null)
        {
            foreach (FrameChange change in changes)
            {
                if (change == null || change.IsOrangeHit) continue;
                cells.Add(change.cell);
                cellLevels.Add(change.toLevel);
            }
        }

        PlayFrameChanges(cells, cellLevels, onDone);
    }

    /// <summary>
    /// One free game's Orange hits (V-9, owner 2026-11-23), all together: the BlueOrb that made each hit
    /// plays once, fades, and leaves the hit's amount on the cell — the end orbs' reveal (RevealOrb),
    /// as the Blue orb, switched on in place with no pop: it is the BlueOrb symbol's art and Spine, so
    /// it takes over from the reel symbol (which GameManager hides) without a visible change. onDone
    /// fires once every reveal, hold included, has finished — straight away when there are none. The
    /// amounts stay until HideHitAmounts.
    /// </summary>
    internal void RevealHits(List<FrameChange> changes, Action onDone)
    {
        int playing = 0;
        bool allStarted = false;

        void OneDone()
        {
            playing--;
            if (allStarted && playing == 0) onDone?.Invoke();
        }

        if (changes != null)
        {
            foreach (FrameChange change in changes)
            {
                if (change == null || !change.IsOrangeHit) continue;
                FrameCell cell = GetCell(change.cell);
                if (cell == null || cell.orb == null) continue;

                if (cell.prize != null) cell.prize.gameObject.SetActive(false);
                HideOrb(cell);

                Sprite resting = OrbSprite(1);
                if (resting != null) cell.orb.sprite = resting;

                Transform t = cell.orb.transform;
                t.DOKill();
                t.localScale = HomeScale(t);
                cell.orb.gameObject.SetActive(true);

                playing++;
                RevealOrb(new EndPrize { cell = change.cell, level = 1, cash = change.orangeHitCash }, null, OneDone);
            }
        }

        allStarted = true;
        if (playing == 0) onDone?.Invoke();
    }

    /// <summary>
    /// The Orange hits' amounts and orbs go, as the next spin starts (owner, 2026-11-23). The frames
    /// stay. Free games never show any other number on the cells, so every prize text goes off.
    /// </summary>
    internal void HideHitAmounts()
    {
        foreach (FrameCell cell in AllCells())
        {
            HideOrb(cell);
            if (cell.prize == null) continue;
            cell.prize.transform.DOKill();
            cell.prize.gameObject.SetActive(false);
        }
    }

    /// <summary>The end phase (§6.1): these positions' frames go, instantly.</summary>
    internal void HideFrames(IEnumerable<int> cells)
    {
        if (cells == null) return;
        foreach (int index in cells)
        {
            FrameCell cell = GetCell(index);
            if (cell == null) continue;
            if (cell.frame != null) cell.frame.gameObject.SetActive(false);
            StopAndHide(cell.effect);
        }
    }

    /// <summary>
    /// The end orbs (§6.2): one per prize, coloured by its level, all popping in at once — from 0,
    /// overshooting, back to scale. Their numbers stay hidden until each is revealed. onDone fires once
    /// the pop has finished.
    /// </summary>
    internal void PopInOrbs(List<EndPrize> prizes, Action onDone)
    {
        bool any = false;

        if (prizes != null)
        {
            foreach (EndPrize prize in prizes)
            {
                FrameCell cell = GetCell(prize.cell);
                if (cell == null || cell.orb == null) continue;

                if (cell.prize != null) cell.prize.gameObject.SetActive(false);
                HideOrb(cell);

                Sprite resting = OrbSprite(prize.level);
                if (resting != null) cell.orb.sprite = resting;

                Transform t = cell.orb.transform;
                Vector3 home = HomeScale(t);
                t.DOKill();
                t.localScale = Vector3.zero;
                cell.orb.gameObject.SetActive(true);
                tweens.Add(t.DOScale(home, orbPopDuration).SetEase(Ease.OutBack, orbPopOvershoot));
                any = true;
            }
        }

        if (!any || orbPopDuration <= 0f)
        {
            onDone?.Invoke();
            return;
        }

        tweens.Add(DOVirtual.DelayedCall(orbPopDuration, () => onDone?.Invoke()));
    }

    /// <summary>
    /// One orb's reveal (§6.3), in order: its jewel Spine plays once (else its still image is held for
    /// orbRevealFallback); the orb fades away; its prize scales in
    /// where it was — onPrizeShown fires then, for TOTAL PRIZE to count up — and holds for prizeHold;
    /// then onDone. The number stays until ResetAll.
    /// </summary>
    internal void RevealOrb(EndPrize prize, Action onPrizeShown, Action onDone)
    {
        FrameCell cell = prize != null ? GetCell(prize.cell) : null;
        if (cell == null)
        {
            onPrizeShown?.Invoke();
            onDone?.Invoke();
            return;
        }

        bool opened = false;
        void Opened()
        {
            if (opened) return;
            opened = true;

            FadeOutOrb(cell, () =>
            {
                HideOrb(cell);
                ShowPrize(cell, prize.cash);
                AudioManager.Instance?.PlayOrbPrize();
                onPrizeShown?.Invoke();
                tweens.Add(DOVirtual.DelayedCall(PrizeHold, () => onDone?.Invoke()));
            });
        }

        // 1. The jewel Spine, once — the controller has no end callback, so timed by its duration.
        if (TryPlayOrbSpine(cell, prize.level, out float spineDuration))
        {
            tweens.Add(DOVirtual.DelayedCall(spineDuration, Opened));
            return;
        }

        // 2. The still image.
        tweens.Add(DOVirtual.DelayedCall(orbRevealFallback, Opened));
    }

    #endregion

    #region Helpers

    // Swaps every listed frame at once and plays the effect over each; calls back once all have played.
    private void PlayFrameChanges(List<int> cells, List<int> levels, Action onDone)
    {
        int playing = 0;
        bool allStarted = false;

        void OneFinished()
        {
            playing--;
            if (allStarted && playing == 0) onDone?.Invoke();
        }

        for (int i = 0; i < cells.Count; i++)
        {
            FrameCell cell = GetCell(cells[i]);
            if (cell == null) continue;

            SetFrame(cell, levels[i]);
            if (PlayEffect(cell, OneFinished)) playing++;
        }

        // The frame effect's sound, once for the whole batch (owner, 2026-11-24).
        if (cells.Count > 0) AudioManager.Instance?.PlayFrameEffect();

        allStarted = true;
        if (playing == 0) onDone?.Invoke();
    }

    // The frame switched on in its colour — a still image, swapped instantly.
    private void SetFrame(FrameCell cell, int level)
    {
        if (cell.frame == null) return;

        Sprite sprite = FrameSprite(level);
        if (sprite != null) cell.frame.sprite = sprite;
        cell.frame.gameObject.SetActive(level > 0);
    }

    // The effect, once, over the frame; it switches off at its end, leaving the frame. False when
    // there is nothing to play.
    private bool PlayEffect(FrameCell cell, Action onFinished)
    {
        ImageAnimation effect = cell.effect;
        if (effect == null || frameEffectFrames == null || frameEffectFrames.Count == 0) return false;

        effect.textureArray = frameEffectFrames;
        effect.doLoopAnimation = false;
        effect.AnimationSpeed = frameEffectSpeed;
        effect.onLoopComplete = loop =>
        {
            effect.onLoopComplete = null;
            StopAndHide(effect);
            onFinished?.Invoke();
        };
        effect.gameObject.SetActive(true);
        effect.StartAnimation();
        return true;
    }

    private void ShowPrize(FrameCell cell, double cash)
    {
        if (cell.prize == null) return;

        // Sprite digits — the Prize texts use the AT-GoldText sprite asset.
        cell.prize.text = SpriteTextFormatter.ToSpriteMoney(cash);

        Transform t = cell.prize.transform;
        Vector3 home = HomeScale(t);
        t.DOKill();
        t.localScale = Vector3.zero;
        cell.prize.gameObject.SetActive(true);
        tweens.Add(t.DOScale(home, prizeScaleInDuration).SetEase(Ease.OutBack));
    }

    // The orb's jewel Spine, started once: the cell's Image component goes off (the Spine is its child,
    // so it still draws) — as SlotView does for a winning symbol. False, with nothing changed, when
    // the cell has no Spine object or there is no jewel Spine (or clip) for this level.
    private bool TryPlayOrbSpine(FrameCell cell, int level, out float duration)
    {
        duration = 0f;
        SpineAnimController spine = cell.orbSpine;
        SkeletonDataAsset asset = OrbSpineAsset(level);
        if (spine == null || asset == null) return false;

        spine.SetSkeletonData(asset);
        duration = spine.GetAnimationDuration();
        if (duration <= 0f) return false;

        spine.gameObject.SetActive(true);
        if (cell.orb != null) cell.orb.enabled = false;

        // Play is ignored while the controller thinks it is still playing — Stop first, every time.
        spine.Stop();
        spine.Play(false);
        return true;
    }

    // Fades whatever is showing the orb — its Spine or its still image — to nothing, then calls back.
    // HideOrb puts the alphas back.
    private void FadeOutOrb(FrameCell cell, Action onFaded)
    {
        var graphics = new List<Graphic>(2);
        if (cell.orb != null && cell.orb.enabled && cell.orb.gameObject.activeInHierarchy) graphics.Add(cell.orb);
        if (cell.orbSpine != null && cell.orbSpine.gameObject.activeInHierarchy && cell.orbSpine.SkeletonGraphic != null)
        {
            graphics.Add(cell.orbSpine.SkeletonGraphic);
        }

        if (graphics.Count == 0 || orbFadeDuration <= 0f)
        {
            onFaded?.Invoke();
            return;
        }

        foreach (Graphic graphic in graphics)
        {
            Graphic g = graphic;
            RememberAlpha(g);
            tweens.Add(DOTween.To(() => g.color.a, a => SetAlpha(g, a), 0f, orbFadeDuration).SetEase(Ease.InQuad));
        }
        tweens.Add(DOVirtual.DelayedCall(orbFadeDuration, () => onFaded?.Invoke()));
    }

    // The orb fully off and back to its resting state: no Spine, the Image component on, every faded
    // alpha restored — ready to pop in again.
    private void HideOrb(FrameCell cell)
    {
        if (cell.orbSpine != null)
        {
            cell.orbSpine.Stop();
            RestoreAlpha(cell.orbSpine.SkeletonGraphic);
            cell.orbSpine.gameObject.SetActive(false);
        }

        if (cell.orb != null)
        {
            RestoreAlpha(cell.orb);
            cell.orb.enabled = true;
            cell.orb.gameObject.SetActive(false);
        }
    }

    // Each faded graphic's alpha as saved in the scene, read the first time it is faded.
    private readonly Dictionary<Graphic, float> homeAlphas = new Dictionary<Graphic, float>();

    private void RememberAlpha(Graphic graphic)
    {
        if (graphic != null && !homeAlphas.ContainsKey(graphic)) homeAlphas[graphic] = graphic.color.a;
    }

    private void RestoreAlpha(Graphic graphic)
    {
        if (graphic != null && homeAlphas.TryGetValue(graphic, out float alpha)) SetAlpha(graphic, alpha);
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        Color color = graphic.color;
        color.a = alpha;
        graphic.color = color;
    }

    // The scale an object was saved with in the scene, read the first time it is popped — so a pop
    // always lands back on it, however often it has been scaled since.
    private readonly Dictionary<Transform, Vector3> homeScales = new Dictionary<Transform, Vector3>();

    private Vector3 HomeScale(Transform t)
    {
        if (!homeScales.TryGetValue(t, out Vector3 scale))
        {
            scale = t.localScale == Vector3.zero ? Vector3.one : t.localScale;
            homeScales[t] = scale;
        }
        return scale;
    }

    private Sprite FrameSprite(int level)
    {
        switch (level)
        {
            case 1: return frameBlue;
            case 2: return framePurple;
            case 3: return frameOrange;
            default: return null;
        }
    }

    private Sprite OrbSprite(int level)
    {
        switch (level)
        {
            case 1: return orbBlue;
            case 2: return orbPurple;
            case 3: return orbYellow;
            default: return null;
        }
    }

    private SkeletonDataAsset OrbSpineAsset(int level)
    {
        switch (level)
        {
            case 1: return orbSpineBlue;
            case 2: return orbSpinePurple;
            case 3: return orbSpineYellow;
            default: return null;
        }
    }

    private FrameCell GetCell(int flatIndex)
    {
        if (flatIndex < 0 || reelCount <= 0) return null;

        int col = flatIndex % reelCount;
        int row = flatIndex / reelCount;
        if (frameColumns == null || col >= frameColumns.Count) return null;

        FrameColumn column = frameColumns[col];
        if (column == null || column.rows == null || row >= column.rows.Count) return null;
        return column.rows[row];
    }

    private IEnumerable<FrameCell> AllCells()
    {
        if (frameColumns == null) yield break;
        foreach (FrameColumn column in frameColumns)
        {
            if (column == null || column.rows == null) continue;
            foreach (FrameCell cell in column.rows)
            {
                if (cell != null) yield return cell;
            }
        }
    }

    private static void StopAndHide(ImageAnimation anim)
    {
        if (anim == null) return;
        anim.onLoopComplete = null;
        anim.StopAnimation();
        anim.gameObject.SetActive(false);
    }

    // Killed without completing, so no callback of an interrupted sequence ever fires.
    private void KillTweens()
    {
        foreach (Tween tween in tweens) tween?.Kill();
        tweens.Clear();
    }

    private void OnDestroy()
    {
        KillTweens();
    }

    #endregion
}

/// <summary>One reel's three frame cells, top to bottom.</summary>
[Serializable]
public class FrameColumn
{
    public List<FrameCell> rows = new List<FrameCell>(3);
}

/// <summary>One board position's frame, effect, end orb and prize.</summary>
[Serializable]
public class FrameCell
{
    [Tooltip("The still frame image — its sprite is swapped Blue / Purple / Orange.")]
    public Image frame;
    [Tooltip("The effect over the frame (Image + ImageAnimation, its own frame list empty). Off between effects.")]
    public ImageAnimation effect;
    [Tooltip("The end orb's Image.")]
    public Image orb;
    [Tooltip("The end orb's jewel Spine — a 'Spine' child of the Orb (SkeletonGraphic + SpineAnimController), saved off. Built and wired by Tools > SL-AT > Build Win Spine Slots. Empty: the orb holds its still image.")]
    public SpineAnimController orbSpine;
    [Tooltip("The prize number at the orb's centre. Stays after the orb goes.")]
    public TMPro.TMP_Text prize;
}
