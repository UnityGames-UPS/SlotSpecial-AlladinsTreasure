using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// The free games' layer over the board (ATFreeGamesBonus.md §5.3, §6): one cell per position, 5 reels
/// x 3 rows, each holding a frame, the effect played on top of it, the end orb and its prize number.
///
/// A frame is a still image, never animated: it is switched on (Blue) or its sprite swapped (Purple,
/// Orange) instantly, and the one effect clip plays once on top of it as it changes. At the end every
/// frame turns into an end orb that pops in, then — one at a time — plays its opening clip, shows its
/// prize and goes, leaving the number.
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
    [Tooltip("One entry per reel, left to right; each holds its 3 cells top to bottom. A cell: Frame (Image), Effect (Image + ImageAnimation, above the frame), Orb (Image + ImageAnimation), Prize (TMP text, above the orb).")]
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
    [Tooltip("Each orb's opening clip, played once as it is revealed. At its end the orb goes and the prize stays.")]
    [SerializeField] private List<Sprite> orbBlueOpenFrames = new List<Sprite>();
    [SerializeField] private List<Sprite> orbPurpleOpenFrames = new List<Sprite>();
    [SerializeField] private List<Sprite> orbYellowOpenFrames = new List<Sprite>();
    [SerializeField] private float orbOpenSpeed = 20f;
    [Tooltip("How long an orb's reveal lasts when it has no opening clip.")]
    [SerializeField] private float orbRevealFallback = 1f;
    [Tooltip("The orbs' pop in: from 0, overshooting, back to their scene scale.")]
    [SerializeField] private float orbPopDuration = 0.4f;
    [SerializeField] private float orbPopOvershoot = 1.7f;
    [Tooltip("The prize number scaling in at the orb's centre.")]
    [SerializeField] private float prizeScaleInDuration = 0.3f;
    [Tooltip("Off: the prize is written as plain text, \"1.00\" (a normal font asset). On: as sprite digits, for a sprite-digit font.")]
    [SerializeField] private bool prizeUsesSpriteDigits;

    // The board's reel count, for turning a flat cell index (row * reelCount + col) into a cell.
    private int reelCount = 5;

    private readonly List<Tween> tweens = new List<Tween>();

    #region Called by GameManager

    /// <summary>The board's reel count, from the init. Flat cell indices are row * reelCount + col.</summary>
    internal void Configure(int boardReelCount)
    {
        if (boardReelCount > 0) reelCount = boardReelCount;
    }

    /// <summary>Everything off and still: no frames, no orbs, no numbers. The base game's state.</summary>
    internal void ResetAll()
    {
        KillTweens();

        foreach (FrameCell cell in AllCells())
        {
            if (cell.frame != null) cell.frame.gameObject.SetActive(false);
            StopAndHide(cell.effect);
            StopAndHide(cell.orbAnimation);
            if (cell.orb != null) cell.orb.gameObject.SetActive(false);
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
    /// An Orange hit (Orange stays Orange, 10x total bet) has NO visual yet — known gap V-9, waiting on
    /// the owner. This is where it goes: today its frame is left as it is.
    /// </summary>
    internal void ApplyChanges(List<FrameChange> changes, Action onDone)
    {
        var cells = new List<int>();
        var cellLevels = new List<int>();

        if (changes != null)
        {
            foreach (FrameChange change in changes)
            {
                if (change == null || change.IsOrangeHit) continue;   // V-9: no visual yet
                cells.Add(change.cell);
                cellLevels.Add(change.toLevel);
            }
        }

        PlayFrameChanges(cells, cellLevels, onDone);
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
                StopAndHide(cell.orbAnimation);

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
    /// How long one orb's reveal lasts — its opening clip, or the fallback when it has none. The
    /// controller counts TOTAL PRIZE up over this, so the count runs while the orb opens (§6.3).
    /// </summary>
    internal float GetRevealDuration(int level)
    {
        List<Sprite> clip = OrbOpenFrames(level);
        if (clip == null || clip.Count == 0 || orbOpenSpeed <= 0f) return orbRevealFallback;

        // ImageAnimation's pace: frames ÷ (24 × speed) seconds a frame, over every frame.
        float frameTime = clip.Count / (24f * orbOpenSpeed);
        return frameTime * clip.Count;
    }

    /// <summary>
    /// One orb's reveal (§6.3): its opening clip plays once and its prize scales in at its centre; at
    /// the clip's end the orb goes and the number stays (until ResetAll). onDone fires then.
    /// </summary>
    internal void RevealOrb(EndPrize prize, Action onDone)
    {
        FrameCell cell = prize != null ? GetCell(prize.cell) : null;
        if (cell == null)
        {
            onDone?.Invoke();
            return;
        }

        ShowPrize(cell, prize.cash);

        bool finished = false;
        void Finish()
        {
            if (finished) return;
            finished = true;
            StopAndHide(cell.orbAnimation);
            if (cell.orb != null) cell.orb.gameObject.SetActive(false);
            onDone?.Invoke();
        }

        List<Sprite> clip = OrbOpenFrames(prize.level);
        ImageAnimation anim = cell.orbAnimation;
        if (anim == null || clip == null || clip.Count == 0)
        {
            tweens.Add(DOVirtual.DelayedCall(orbRevealFallback, Finish));
            return;
        }

        anim.textureArray = clip;
        anim.doLoopAnimation = false;
        anim.AnimationSpeed = orbOpenSpeed;
        anim.onLoopComplete = loop => { anim.onLoopComplete = null; Finish(); };
        anim.gameObject.SetActive(true);
        anim.StartAnimation();

        // A safety net only, so a clip stopped from outside can never stall the reveal.
        tweens.Add(DOVirtual.DelayedCall(GetRevealDuration(prize.level) + 1f, Finish));
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

        cell.prize.text = prizeUsesSpriteDigits
            ? SpriteTextFormatter.ToSpriteMoney(cash)
            : cash.ToString(SpriteTextFormatter.MoneyFormat);

        Transform t = cell.prize.transform;
        Vector3 home = HomeScale(t);
        t.DOKill();
        t.localScale = Vector3.zero;
        cell.prize.gameObject.SetActive(true);
        tweens.Add(t.DOScale(home, prizeScaleInDuration).SetEase(Ease.OutBack));
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

    private List<Sprite> OrbOpenFrames(int level)
    {
        switch (level)
        {
            case 1: return orbBlueOpenFrames;
            case 2: return orbPurpleOpenFrames;
            case 3: return orbYellowOpenFrames;
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
    [Tooltip("The end orb's ImageAnimation — on the Orb object itself, or a child over it. Plays the opening clip.")]
    public ImageAnimation orbAnimation;
    [Tooltip("The prize number at the orb's centre. Stays after the orb goes.")]
    public TMPro.TMP_Text prize;
}
