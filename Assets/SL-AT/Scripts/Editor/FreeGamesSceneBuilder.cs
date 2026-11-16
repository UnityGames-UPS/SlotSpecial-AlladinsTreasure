using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ONE-OFF editor tool (owner's exception to the "never wire the Inspector" rule, 2026-11-15): builds
/// the free games' scene objects and wires them to their scripts — ATFreeGamesBonus.md §13.3.
///
/// Run with GameScene open: Tools > Aladdin's Treasure > Build Free Games Objects. Everything it does is
/// ONE undo step (Ctrl+Z removes it all), and it refuses to run a second time (a FramesView already in
/// the scene). It never saves — check the result, then save the scene yourself.
///
/// It builds structure, components and references only. Every sprite, clip and font is dragged in by
/// hand afterwards; until then the empty Images show as white boxes, which helps with positioning.
/// Wild Jokers' leftovers (FreeGamesRemainingText, the old PopupImage tree, BlackFlim) are left alone.
///
/// Delete this script, and the Editor folder, once it has been run and the scene saved.
/// </summary>
internal static class FreeGamesSceneBuilder
{
    private const string MenuPath = "Tools/Aladdin's Treasure/Build Free Games Objects";
    private const string UndoName = "Build Free Games Objects";

    // Where the top panel and TOTAL PRIZE sit inside FreeGames — WJ's "FREE GAME X OF Y" panel's spot,
    // above the reels. Placeholder sizes; the owner positions everything by eye once the art is in.
    private static readonly Vector2 TopPanelPosition = new Vector2(0f, 410f);
    private static readonly Vector2 TopPanelSize = new Vector2(700f, 120f);

    private static readonly List<string> Report = new List<string>();
    private static TMP_FontAsset font;

    [MenuItem(MenuPath)]
    private static void Build()
    {
        Report.Clear();

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Fail("Leave Play mode first.");
            return;
        }

        // Everything the build hangs off, found in the open scene (inactive objects included).
        GameManager gameManager = FindOne<GameManager>();
        SlotView slotView = FindOne<SlotView>();
        UIManager uiManager = FindOne<UIManager>();
        FreeGameView freeGameView = FindOne<FreeGameView>();
        FeaturePopupView popupView = FindOne<FeaturePopupView>();

        if (gameManager == null || slotView == null || uiManager == null || freeGameView == null || popupView == null)
        {
            Fail("Open GameScene first: it needs a GameManager, SlotView, UIManager, FreeGameView and FeaturePopupView.");
            return;
        }

        if (FindOne<FramesView>() != null)
        {
            Fail("A FramesView is already in the scene — the free games' objects have been built. Nothing done.");
            return;
        }

        var slotSo = new SerializedObject(slotView);
        GameObject animationLayer = slotSo.FindProperty("winAnimationLayer")?.objectReferenceValue as GameObject;
        if (animationLayer == null)
        {
            Fail("SlotView's Win Animation Layer (AnimationLayer) isn't wired — the frames copy its layout.");
            return;
        }

        Image[,] cellRows = ReadAnimSlotRows(slotSo);
        if (cellRows == null)
        {
            Fail("SlotView's Anim Slot Columns aren't fully wired (5 columns x 3 rows) — the frame cells copy their layout.");
            return;
        }

        Transform slotBG = freeGameView.transform.parent;
        Transform bg = slotBG != null ? slotBG.parent : null;
        if (bg == null)
        {
            Fail("FreeGames isn't where expected (MainCanvas/BG/SlotBG/FreeGames).");
            return;
        }

        var uiSo = new SerializedObject(uiManager);
        TMP_Text winText = uiSo.FindProperty("winAmountText")?.objectReferenceValue as TMP_Text;
        font = winText != null && winText.font != null ? winText.font : TMP_Settings.defaultFontAsset;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);

        GameObject frames = BuildFrames(animationLayer, cellRows, gameManager);
        BuildScreen(bg, freeGameView);
        BuildPopups(popupView);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(gameManager.gameObject.scene);
        Selection.activeGameObject = frames;

        Debug.Log("[FreeGamesSceneBuilder] Built and wired the free games' objects (one undo step):\n - " +
                  string.Join("\n - ", Report) +
                  "\nStill to do by hand: every sprite, clip and font (the white boxes), final positions and sizes, " +
                  "then save the scene and delete this script. See ATFreeGamesBonus.md §13.3.");
        EditorUtility.DisplayDialog("Free Games objects built",
            "Built and wired. Check the hierarchy and the Inspector references, then save the scene.\n\n" +
            "Ctrl+Z undoes the whole build. Details are in the Console.", "OK");
    }

    #region Frames (FramesView)

    // SlotObject/Frames, right after AnimationLayer so it draws over the win animations: 5 columns x 3
    // cells, each laid out exactly like the AnimationLayer slot it sits over.
    private static GameObject BuildFrames(GameObject animationLayer, Image[,] cellRows, GameManager gameManager)
    {
        Transform slotObject = animationLayer.transform.parent;

        GameObject frames = CreateUI("Frames", slotObject);
        CopyRect(animationLayer.GetComponent<RectTransform>(), frames.GetComponent<RectTransform>());
        frames.transform.SetSiblingIndex(animationLayer.transform.GetSiblingIndex() + 1);
        FramesView view = frames.AddComponent<FramesView>();

        var framesSo = new SerializedObject(view);
        SerializedProperty columns = framesSo.FindProperty("frameColumns");
        columns.arraySize = 5;

        for (int col = 0; col < 5; col++)
        {
            RectTransform slotColumn = cellRows[col, 0].rectTransform.parent as RectTransform;
            GameObject column = CreateUI($"FrameColumn ({col})", frames.transform);
            CopyRect(slotColumn, column.GetComponent<RectTransform>());

            SerializedProperty rows = columns.GetArrayElementAtIndex(col).FindPropertyRelative("rows");
            rows.arraySize = 3;

            for (int row = 0; row < 3; row++)
            {
                GameObject cell = CreateUI($"FrameCell ({row})", column.transform);
                CopyRect(cellRows[col, row].rectTransform, cell.GetComponent<RectTransform>());

                Image frame = CreateImage("Frame", cell.transform, active: false);
                ImageAnimation effect = CreateAnimatedImage("Effect", cell.transform);
                ImageAnimation orbAnim = CreateAnimatedImage("Orb", cell.transform);
                TMP_Text prize = CreateText("Prize", cell.transform, "0.00", active: false);

                SerializedProperty entry = rows.GetArrayElementAtIndex(row);
                entry.FindPropertyRelative("frame").objectReferenceValue = frame;
                entry.FindPropertyRelative("effect").objectReferenceValue = effect;
                entry.FindPropertyRelative("orb").objectReferenceValue = orbAnim.GetComponent<Image>();
                entry.FindPropertyRelative("orbAnimation").objectReferenceValue = orbAnim;
                entry.FindPropertyRelative("prize").objectReferenceValue = prize;
            }
        }

        framesSo.ApplyModifiedPropertiesWithoutUndo();   // the object is new: its creation is the undo
        Report.Add("SlotObject/Frames (FramesView) after AnimationLayer: 5 columns x 3 cells (Frame, Effect, Orb, Prize), Frame Columns wired");

        Wire(gameManager, ("framesView", view));
        Report.Add("GameManager.framesView -> Frames");

        return frames;
    }

    #endregion

    #region Screen (FreeGameView)

    // The free-games background (BG's first child, over BG's own base image), the top panel and TOTAL
    // PRIZE in FreeGames, and the full-screen transition (before PopupObject, so error popups stay on top).
    private static void BuildScreen(Transform bg, FreeGameView view)
    {
        GameObject background = CreateUI("FreeGamesBackground", bg);
        Fill(background.GetComponent<RectTransform>());
        AddImage(background, raycast: false);
        background.transform.SetSiblingIndex(0);
        background.SetActive(false);

        Transform root = view.transform;

        GameObject topPanel = CreateUI("TopPanel", root);
        Place(topPanel, TopPanelPosition, TopPanelSize);
        AddImage(topPanel, raycast: false);

        GameObject remaining = CreateUI("RemainingState", topPanel.transform);
        Fill(remaining.GetComponent<RectTransform>());
        TMP_Text remainingCount = CreateText("RemainingCountText", remaining.transform, "10", active: true);
        Place(remainingCount.gameObject, new Vector2(-260f, 0f), new Vector2(140f, 100f));
        Image remainingLabel = CreateImage("RemainingLabel", remaining.transform, active: true);
        Place(remainingLabel.gameObject, new Vector2(70f, 0f), new Vector2(480f, 100f));

        Image lastSpin = CreateImage("LastSpinState", topPanel.transform, active: false);
        Place(lastSpin.gameObject, Vector2.zero, new Vector2(600f, 100f));
        Image featureComplete = CreateImage("FeatureCompleteState", topPanel.transform, active: false);
        Place(featureComplete.gameObject, Vector2.zero, new Vector2(600f, 100f));
        topPanel.SetActive(false);

        GameObject totalPrize = CreateUI("TotalPrizePanel", root);
        Place(totalPrize, TopPanelPosition, TopPanelSize);
        AddImage(totalPrize, raycast: false);
        Image totalPrizeLabel = CreateImage("TotalPrizeLabel", totalPrize.transform, active: true);
        Place(totalPrizeLabel.gameObject, new Vector2(-170f, 0f), new Vector2(320f, 90f));
        TMP_Text totalPrizeValue = CreateText("TotalPrizeValueText", totalPrize.transform, string.Empty, active: true);
        Place(totalPrizeValue.gameObject, new Vector2(190f, 0f), new Vector2(300f, 100f));
        totalPrize.SetActive(false);

        GameObject transition = CreateUI("FreeGamesTransition", bg);
        Fill(transition.GetComponent<RectTransform>());
        Image transitionImage = AddImage(transition, raycast: true);   // blocks input while it plays
        transitionImage.preserveAspect = false;
        AddAnimation(transition, transitionImage);
        Transform popupObject = bg.Find("PopupObject");
        transition.transform.SetSiblingIndex(popupObject != null ? popupObject.GetSiblingIndex() : bg.childCount - 1);
        transition.SetActive(false);

        Wire(view,
            ("transition", transition.GetComponent<ImageAnimation>()),
            ("freeGamesBackground", background),
            ("topPanel", topPanel),
            ("remainingState", remaining),
            ("remainingCountText", remainingCount),
            ("lastSpinState", lastSpin.gameObject),
            ("featureCompleteState", featureComplete.gameObject),
            ("totalPrizePanel", totalPrize.GetComponent<RectTransform>()),
            ("totalPrizeValueText", totalPrizeValue));

        Report.Add("BG/FreeGamesBackground (BG's first child; BG's own image stays the base background — Base Game Background left empty)");
        Report.Add("FreeGames/TopPanel (RemainingState: RemainingCountText + RemainingLabel; LastSpinState; FeatureCompleteState) and FreeGames/TotalPrizePanel (TotalPrizeLabel, TotalPrizeValueText)");
        Report.Add($"BG/FreeGamesTransition ({(popupObject != null ? "before PopupObject" : "last child of BG")}); FreeGameView fields wired");
    }

    #endregion

    #region Popups (FeaturePopupView)

    // "N FREE GAMES" and CONGRATULATIONS / COLLECT, centred in FeaturePopupView, both saved off.
    private static void BuildPopups(FeaturePopupView view)
    {
        Transform root = view.transform;

        GameObject awarded = CreateUI("AwardedPopup", root);
        Place(awarded, Vector2.zero, new Vector2(900f, 500f));
        AddImage(awarded, raycast: false);
        TMP_Text awardedCount = CreateText("AwardedCountText", awarded.transform, "10", active: true);
        Place(awardedCount.gameObject, new Vector2(0f, 70f), new Vector2(400f, 200f));
        Image plural = CreateImage("AwardedLabelPlural", awarded.transform, active: true);
        Place(plural.gameObject, new Vector2(0f, -120f), new Vector2(600f, 120f));
        Image singular = CreateImage("AwardedLabelSingular", awarded.transform, active: false);
        Place(singular.gameObject, new Vector2(0f, -120f), new Vector2(600f, 120f));
        awarded.SetActive(false);

        GameObject congratulations = CreateUI("CongratulationsPopup", root);
        Place(congratulations, Vector2.zero, new Vector2(900f, 560f));
        AddImage(congratulations, raycast: false);
        Image congratsLabel = CreateImage("CongratulationsLabel", congratulations.transform, active: true);
        Place(congratsLabel.gameObject, new Vector2(0f, 150f), new Vector2(760f, 160f));
        TMP_Text amount = CreateText("CongratulationsAmountText", congratulations.transform, "0.00", active: true);
        Place(amount.gameObject, new Vector2(0f, -10f), new Vector2(600f, 140f));

        GameObject collect = CreateUI("CollectButton", congratulations.transform);
        Place(collect, new Vector2(0f, -190f), new Vector2(320f, 100f));
        Image collectImage = AddImage(collect, raycast: true);
        Button button = collect.AddComponent<Button>();
        button.targetGraphic = collectImage;
        button.transition = Selectable.Transition.ColorTint;   // greyed by its disabled colour until the count-up ends
        congratulations.SetActive(false);

        Wire(view,
            ("awardedPopup", awarded.GetComponent<RectTransform>()),
            ("awardedCountText", awardedCount),
            ("awardedLabelPlural", plural.gameObject),
            ("awardedLabelSingular", singular.gameObject),
            ("congratulationsPopup", congratulations),
            ("congratulationsAmountText", amount),
            ("collectButton", button));

        Report.Add("FeaturePopupView/AwardedPopup (AwardedCountText, AwardedLabelPlural, AwardedLabelSingular) and FeaturePopupView/CongratulationsPopup (CongratulationsLabel, CongratulationsAmountText, CollectButton); FeaturePopupView fields wired (COLLECT's click is added in code — leave its OnClick empty)");
    }

    #endregion

    #region Helpers

    // SlotView.animSlotColumns[c].rows[r].image — the AnimationLayer slots the frame cells sit over.
    private static Image[,] ReadAnimSlotRows(SerializedObject slotSo)
    {
        SerializedProperty columns = slotSo.FindProperty("animSlotColumns");
        if (columns == null || columns.arraySize < 5) return null;

        var rows = new Image[5, 3];
        for (int col = 0; col < 5; col++)
        {
            SerializedProperty colRows = columns.GetArrayElementAtIndex(col).FindPropertyRelative("rows");
            if (colRows == null || colRows.arraySize < 3) return null;

            for (int row = 0; row < 3; row++)
            {
                Image image = colRows.GetArrayElementAtIndex(row).FindPropertyRelative("image")?.objectReferenceValue as Image;
                if (image == null || image.rectTransform.parent == null) return null;
                rows[col, row] = image;
            }
        }
        return rows;
    }

    // A new UI object under parent, on its layer, registered for undo (undoing removes it and everything
    // under it).
    private static GameObject CreateUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(go, UndoName);
        return go;
    }

    private static Image CreateImage(string name, Transform parent, bool active)
    {
        GameObject go = CreateUI(name, parent);
        Fill(go.GetComponent<RectTransform>());
        Image image = AddImage(go, raycast: false);
        go.SetActive(active);
        return image;
    }

    // An Image + ImageAnimation, filling its parent, saved off. Its frame list stays empty (the code or
    // the owner fills it); it never starts on its own.
    private static ImageAnimation CreateAnimatedImage(string name, Transform parent)
    {
        GameObject go = CreateUI(name, parent);
        Fill(go.GetComponent<RectTransform>());
        Image image = AddImage(go, raycast: false);
        ImageAnimation anim = AddAnimation(go, image);
        go.SetActive(false);
        return anim;
    }

    private static Image AddImage(GameObject go, bool raycast)
    {
        Image image = go.AddComponent<Image>();
        image.raycastTarget = raycast;
        image.preserveAspect = true;
        return image;
    }

    private static ImageAnimation AddAnimation(GameObject go, Image image)
    {
        ImageAnimation anim = go.AddComponent<ImageAnimation>();
        anim.textureArray = new List<Sprite>();
        anim.rendererDelegate = image;
        anim.doLoopAnimation = false;
        return anim;
    }

    private static TMP_Text CreateText(string name, Transform parent, string text, bool active)
    {
        GameObject go = CreateUI(name, parent);
        Fill(go.GetComponent<RectTransform>());

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 18f;
        tmp.fontSizeMax = 96f;
        tmp.raycastTarget = false;

        go.SetActive(active);
        return tmp;
    }

    // Centre-anchored at a position and size inside its parent.
    private static void Place(GameObject go, Vector2 position, Vector2 size)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    // Stretched over its parent.
    private static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void CopyRect(RectTransform from, RectTransform to)
    {
        to.anchorMin = from.anchorMin;
        to.anchorMax = from.anchorMax;
        to.pivot = from.pivot;
        to.anchoredPosition = from.anchoredPosition;
        to.sizeDelta = from.sizeDelta;
        to.localScale = from.localScale;
        to.localRotation = from.localRotation;
    }

    // Sets serialized references on an existing component, with undo. A field that doesn't exist is
    // reported instead of silently skipped.
    private static void Wire(Object target, params (string field, Object value)[] fields)
    {
        var so = new SerializedObject(target);
        foreach (var (field, value) in fields)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[FreeGamesSceneBuilder] {target.GetType().Name} has no field '{field}' — not wired.");
                continue;
            }
            prop.objectReferenceValue = value;
        }
        so.ApplyModifiedProperties();
    }

    private static T FindOne<T>() where T : Object
    {
        T[] found = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return found.Length > 0 ? found[0] : null;
    }

    private static void Fail(string message)
    {
        Debug.LogWarning("[FreeGamesSceneBuilder] " + message);
        EditorUtility.DisplayDialog("Build Free Games Objects", message, "OK");
    }

    #endregion
}
