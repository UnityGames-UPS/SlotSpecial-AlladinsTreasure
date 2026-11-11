using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// The Sandbell Pick's own presentation (SandbellPickBonus.md §5, §7): the pick cells over the
/// Sandbells — the click targets, the pulsing PICK ME, the revealed X values and the explosion. The
/// Sandbells' clip and win boxes are SlotView's (the animation layer); the popups are
/// FeaturePopupView's.
///
/// View layer only: GameManager opens the pick and is called back with the clicked cell. It never
/// calls into the game loop. DOTween only, no coroutines, so it can sit on PickCells even while the
/// animation layer above it is off. Every reference is optional — a pick with nothing to click calls
/// straight back, so the feature never waits forever.
/// </summary>
public class SandbellPickView : MonoBehaviour
{
    [Header("Pick cells")]
    [Tooltip("The pick cells (AnimationLayer/PickCells) — one entry per reel column, each holding its 3 cells top to bottom, laid out like the win boxes. Each cell: its Button (the click target), PICK ME, the value text, and the explosion.")]
    [SerializeField] private List<PickCellColumn> pickCellColumns = new List<PickCellColumn>(5);

    [Tooltip("The explosion's frames, played once on the clicked Sandbell. One clip for every cell, handed to them at runtime — their own frame lists stay empty. Empty: no explosion.")]
    [SerializeField] private List<Sprite> explosionFrames = new List<Sprite>();

    [Header("Value sprite assets")]
    [Tooltip("The clicked Sandbell's value (1-0 GREEN). Must carry an \"x\" as sprite 11. Empty: the text keeps its own sprite asset.")]
    [SerializeField] private TMPro.TMP_SpriteAsset pickedValueSpriteAsset;

    [Tooltip("The other Sandbells' values (0-1ORANGE). Must carry an \"x\" as sprite 11. Empty: the text keeps its own sprite asset.")]
    [SerializeField] private TMPro.TMP_SpriteAsset missedValueSpriteAsset;

    // PICK ME's pulse: up to this scale and back, each half this long, until the pick.
    private const float PickMePulseScale = 1.12f;
    private const float PickMePulseDuration = 0.45f;
    // The clicked Sandbell's value is drawn this much larger than the others'.
    private const float PickedValueScale = 1.5f;

    // The pick in progress: the Sandbells (flat indices, board order), the board's reel count to read
    // them with, whether a click is still awaited, and who to tell.
    private readonly List<int> pickCells = new List<int>();
    private int reelCount = 5;
    private bool pickOpen;
    private Action<int> onPicked;

    private readonly List<Tween> pulseTweens = new List<Tween>();

    // No Start / Awake work on purpose: this sits under the animation layer, which SlotView switches
    // off and on, and a deferred Start could run mid-pick. GameManager hides the cells at init instead.

    #region Called by GameManager

    /// <summary>
    /// Opens the pick: every Sandbell's cell comes on with a pulsing PICK ME and takes clicks.
    /// onPicked fires once, with the flat index of the Sandbell clicked. No time limit and no automatic
    /// pick (owner) — except that a pick with no cell wired to click takes the first Sandbell, rather
    /// than wait forever.
    /// </summary>
    internal void ShowPick(List<int> cells, int boardReelCount, Action<int> onPickedCallback)
    {
        HideImmediate();

        reelCount = Mathf.Max(1, boardReelCount);
        if (cells != null) pickCells.AddRange(cells);
        onPicked = onPickedCallback;
        pickOpen = true;

        bool anyClickable = false;
        foreach (int flatIndex in pickCells)
        {
            PickCell cell = GetCell(flatIndex);
            if (cell == null || cell.button == null) continue;

            int picked = flatIndex;
            cell.button.onClick.RemoveAllListeners();
            cell.button.onClick.AddListener(() => OnCellClicked(picked));
            cell.button.interactable = true;
            cell.button.gameObject.SetActive(true);
            anyClickable = true;

            if (cell.valueText != null) cell.valueText.gameObject.SetActive(false);
            if (cell.explosion != null) cell.explosion.gameObject.SetActive(false);

            if (cell.pickMe != null)
            {
                cell.pickMe.localScale = Vector3.one;
                cell.pickMe.gameObject.SetActive(true);
                pulseTweens.Add(cell.pickMe.DOScale(PickMePulseScale, PickMePulseDuration)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo));
            }
        }

        if (!anyClickable)
        {
            Debug.LogWarning("[SandbellPickView] No pick cells are wired for these Sandbells — picking the first one.");
            OnCellClicked(pickCells.Count > 0 ? pickCells[0] : -1);
        }
    }

    /// <summary>
    /// The reveal: PICK ME gives way to the X values — the winning one, larger, on the clicked
    /// Sandbell; the missed ones on the others in board order (owner: the simplest). The explosion
    /// plays on the clicked one only. The values stay until HideImmediate.
    /// </summary>
    internal void Reveal(int pickedCell, double winValue, List<double> missedValues)
    {
        KillPulses();

        int nextMissed = 0;
        foreach (int flatIndex in pickCells)
        {
            PickCell cell = GetCell(flatIndex);
            if (cell == null) continue;

            if (cell.pickMe != null)
            {
                cell.pickMe.localScale = Vector3.one;
                cell.pickMe.gameObject.SetActive(false);
            }

            if (cell.valueText == null) continue;

            bool isPicked = flatIndex == pickedCell;
            bool hasValue = isPicked || (missedValues != null && nextMissed < missedValues.Count);
            if (!hasValue)
            {
                cell.valueText.gameObject.SetActive(false);
                continue;
            }

            double value = isPicked ? winValue : missedValues[nextMissed++];

            // The colour is the sprite asset: green on the clicked Sandbell, orange on the rest.
            TMPro.TMP_SpriteAsset spriteAsset = isPicked ? pickedValueSpriteAsset : missedValueSpriteAsset;
            if (spriteAsset != null) cell.valueText.spriteAsset = spriteAsset;
            cell.valueText.text = SpriteTextFormatter.ToSpriteMultiplier(value);
            cell.valueText.transform.localScale = Vector3.one * (isPicked ? PickedValueScale : 1f);
            cell.valueText.gameObject.SetActive(true);
        }

        PlayExplosion(GetCell(pickedCell));
    }

    /// <summary>
    /// Every cell off, with nothing left to click or pulse. GameManager calls it at startup and as
    /// every spin starts, so the cells show only during a pick and its reveal.
    /// </summary>
    internal void HideImmediate()
    {
        KillPulses();
        pickOpen = false;
        onPicked = null;
        pickCells.Clear();

        if (pickCellColumns == null) return;
        foreach (var column in pickCellColumns)
        {
            if (column == null || column.rows == null) continue;
            foreach (var cell in column.rows)
            {
                if (cell == null) continue;

                if (cell.pickMe != null) cell.pickMe.localScale = Vector3.one;
                if (cell.valueText != null) cell.valueText.transform.localScale = Vector3.one;
                if (cell.explosion != null)
                {
                    cell.explosion.onLoopComplete = null;
                    cell.explosion.StopAnimation();
                    cell.explosion.gameObject.SetActive(false);
                }
                if (cell.button != null)
                {
                    cell.button.onClick.RemoveAllListeners();
                    cell.button.gameObject.SetActive(false);
                }
            }
        }
    }

    #endregion

    #region Helpers

    // The player's click. Taken once: the pick closes and every cell stops taking clicks.
    private void OnCellClicked(int flatIndex)
    {
        if (!pickOpen) return;
        pickOpen = false;

        foreach (int other in pickCells)
        {
            PickCell cell = GetCell(other);
            if (cell != null && cell.button != null) cell.button.interactable = false;
        }

        var callback = onPicked;
        onPicked = null;
        callback?.Invoke(flatIndex);
    }

    // Plays the explosion once on one cell, then hides it. Skipped until the clip exists — the
    // explosion's Image has no sprite of its own and would show as a plain square.
    private void PlayExplosion(PickCell cell)
    {
        if (cell == null || cell.explosion == null) return;
        if (explosionFrames == null || explosionFrames.Count == 0) return;

        ImageAnimation explosion = cell.explosion;
        explosion.textureArray = explosionFrames;
        explosion.doLoopAnimation = false;
        // Speed = frame count plays it at 24 fps (ImageAnimation: frames² ÷ (24 × speed) a loop).
        explosion.AnimationSpeed = explosionFrames.Count;
        explosion.onLoopComplete = _ =>
        {
            explosion.onLoopComplete = null;
            explosion.StopAnimation();
            explosion.gameObject.SetActive(false);
        };
        explosion.gameObject.SetActive(true);
        explosion.StartAnimation();
    }

    // The cell over one visible board cell (flat index row * reelCount + col), or null when unwired.
    private PickCell GetCell(int flatIndex)
    {
        if (flatIndex < 0) return null;

        int row = flatIndex / reelCount;
        int col = flatIndex % reelCount;

        if (pickCellColumns == null || col >= pickCellColumns.Count) return null;
        var column = pickCellColumns[col];
        if (column == null || column.rows == null || row >= column.rows.Count) return null;

        return column.rows[row];
    }

    private void KillPulses()
    {
        foreach (var tween in pulseTweens) tween?.Kill();
        pulseTweens.Clear();
    }

    private void OnDestroy()
    {
        KillPulses();
    }

    #endregion

#if UNITY_EDITOR
    #region Editor — fill the pick cells

    /// <summary>
    /// Editor only (right-click the component → "Fill pick cells from children"): fills
    /// pickCellColumns from the hierarchy under this object, so the 60 references needn't be dragged
    /// by hand. Reads the structure, not names: each child is a column (left to right), each of its
    /// children a cell (top to bottom). In a cell: its own Button; the value is the TMP text inside it;
    /// the explosion the ImageAnimation inside it; PICK ME the remaining child. Undoable; save the scene
    /// afterwards. Anything missing is logged and left empty.
    /// </summary>
    [ContextMenu("Fill pick cells from children")]
    private void FillPickCellsFromChildren()
    {
        UnityEditor.Undo.RecordObject(this, "Fill pick cells from children");

        var columns = new List<PickCellColumn>();
        int cellsFilled = 0;
        int problems = 0;

        for (int c = 0; c < transform.childCount; c++)
        {
            Transform columnTransform = transform.GetChild(c);
            var column = new PickCellColumn();

            for (int r = 0; r < columnTransform.childCount; r++)
            {
                Transform cellTransform = columnTransform.GetChild(r);
                var cell = new PickCell
                {
                    button = cellTransform.GetComponent<Button>(),
                    valueText = cellTransform.GetComponentInChildren<TMPro.TMP_Text>(true),
                    explosion = cellTransform.GetComponentInChildren<ImageAnimation>(true)
                };

                // PICK ME: the first child that is neither the value nor the explosion.
                foreach (Transform child in cellTransform)
                {
                    if (cell.valueText != null && child == cell.valueText.transform) continue;
                    if (cell.explosion != null && child == cell.explosion.transform) continue;
                    cell.pickMe = child as RectTransform;
                    break;
                }

                string where = $"{columnTransform.name} / {cellTransform.name}";
                if (cell.button == null) { problems++; Debug.LogWarning($"[SandbellPickView] {where}: no Button on the cell.", cellTransform); }
                if (cell.pickMe == null) { problems++; Debug.LogWarning($"[SandbellPickView] {where}: no PICK ME child found.", cellTransform); }
                if (cell.valueText == null) { problems++; Debug.LogWarning($"[SandbellPickView] {where}: no TMP value text found.", cellTransform); }
                if (cell.explosion == null) { problems++; Debug.LogWarning($"[SandbellPickView] {where}: no explosion ImageAnimation found.", cellTransform); }

                column.rows.Add(cell);
                cellsFilled++;
            }

            columns.Add(column);
        }

        pickCellColumns = columns;
        UnityEditor.EditorUtility.SetDirty(this);

        Debug.Log($"[SandbellPickView] Filled {columns.Count} columns, {cellsFilled} cells" +
                  (problems > 0 ? $" — {problems} references missing, see the warnings." : " — all references found. Save the scene to keep them."), this);
    }

    #endregion
#endif
}

// One pick cell (AnimationLayer/PickCells/PickColumn/PickCell): the click target over one visible
// cell and what is drawn on it. Wired explicitly, like the win boxes.
[Serializable]
public class PickCell
{
    public Button button;                  // the PickCell itself — a transparent Image + Button
    public RectTransform pickMe;           // PICK ME, pulsing until the pick
    public TMPro.TMP_Text valueText;       // the X value, revealed after the pick
    public ImageAnimation explosion;       // played once on the clicked Sandbell
}

// One reel column's pick cells, top row first — the same layout as WinBoxColumn.
[Serializable]
public class PickCellColumn
{
    public List<PickCell> rows = new List<PickCell>(3);   // index 0 = top active row
}
