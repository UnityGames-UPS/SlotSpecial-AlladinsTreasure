using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Spine.Unity;

public class SlotView : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameManager gameManager;

    // Number of distinct symbols the backend can send (ids 0..SymbolCount-1). Every array indexed
    // by symbol id is sized from this, so the count lives in exactly one place.
    private const int SymbolCount = 13;

    [Header("Symbol Sprites - Assign by Name")]
    // Field names follow the backend's symbol "name", so the two can be checked against each other
    // at a glance. The ids are the init's symbol ids and are what the server's matrix carries — if
    // the backend ever reorders the table, BuildSymbolSpriteArray is what to correct.
    // Beware the two low cards at the end: id 9 is named "10" and id 10 is named "9".
    [SerializeField] private Sprite spriteFairy;              // ID: 0  (high — top paytable)
    [SerializeField] private Sprite spriteUnicorn;            // ID: 1  (high)
    [SerializeField] private Sprite spriteSquirrel;           // ID: 2  (high)
    [SerializeField] private Sprite spriteMushrooms;          // ID: 3  (high)
    [SerializeField] private Sprite spriteLotus;              // ID: 4  (high)
    [SerializeField] private Sprite spriteA;                  // ID: 5  (low)
    [SerializeField] private Sprite spriteK;                  // ID: 6  (low)
    [SerializeField] private Sprite spriteQ;                  // ID: 7  (low)
    [SerializeField] private Sprite spriteJ;                  // ID: 8  (low)
    [SerializeField] private Sprite spriteTen;                // ID: 9  (low — named "10")
    [SerializeField] private Sprite spriteNine;               // ID: 10 (low — named "9")
    [SerializeField] private Sprite spriteWild;               // ID: 11 (wild — reels 2-4 in the base game, 2-5 in free games)
    [SerializeField] private Sprite spriteBlueOrb;            // ID: 12 (scatter — 3 or more trigger free games)

    // Rect size for each symbol, keyed by symbol id. Any id not listed here uses normalSymbolSize.
    //
    // Every symbol is listed, all at DefaultSymbolSize (255, owner, 2026-11-18) until each is sized
    // against its own art. Sizing is per-symbol art, not a role: anything above the 245 pitch
    // (symbolHeight) overlaps its vertical neighbours — 255 does, by 10 — which is intentional bleed
    // but also means it swallows clicks aimed at the cells above and below.
    //
    // Also the win-layer cells' size, and so the Spine animations' size: their Spine objects are
    // stretched to the cell and scale with it (Layout Scale "Fit In Parent", set by SpineBuilder).
    //
    // Kept next to the sprite fields on purpose: both are id-keyed maps of the same symbol table, so
    // if the backend ever reorders it they have to be corrected together.
    private static readonly Vector2 DefaultSymbolSize = new Vector2(255f, 255f);
    private static readonly Dictionary<int, Vector2> SymbolSizeOverrides = new Dictionary<int, Vector2>
    {
        { 0, DefaultSymbolSize},  // Fairy
        { 1, DefaultSymbolSize},  // Unicorn
        { 2, DefaultSymbolSize},  // Squirrel
        { 3, DefaultSymbolSize},  // Mushrooms
        { 4, DefaultSymbolSize},  // Lotus
        { 5, DefaultSymbolSize},  // A
        { 6, DefaultSymbolSize},  // K
        { 7, DefaultSymbolSize},  // Q
        { 8, DefaultSymbolSize},  // J
        { 9, DefaultSymbolSize},  // "10"
        {10, DefaultSymbolSize},  // "9"
        {11, DefaultSymbolSize},  // Wild
        {12, DefaultSymbolSize}   // BlueOrb
    };

    // Playback speed per symbol, applied wherever that symbol's clip is assigned.
    //
    // NOT frames per second. ImageAnimation derives its frame delay as
    // (1/24) * frameCount / AnimationSpeed, so the same value plays a long clip more slowly than a
    // short one — which is why each symbol needs its own, tuned by eye against its own art.
    //
    // Every symbol gets a value on every write. The win-layer components are reused from spin to
    // spin, so leaving one untouched would silently inherit whatever the previous symbol had set
    // on that slot.
    //
    // All 13 are listed explicitly, all at the default until each is tuned against its own clip.
    // The fallback below is only reached if the backend ever sends an id this table doesn't know
    // about.
    private const float DefaultSymbolAnimationSpeed = 20f;
    //Animation Speeds
    private static readonly Dictionary<int, float> SymbolAnimationSpeeds = new Dictionary<int, float>
    {
        { 0, DefaultSymbolAnimationSpeed },  // Fairy
        { 1, DefaultSymbolAnimationSpeed },  // Unicorn
        { 2, DefaultSymbolAnimationSpeed },  // Squirrel
        { 3, DefaultSymbolAnimationSpeed },  // Mushrooms
        { 4, DefaultSymbolAnimationSpeed },  // Lotus
        { 5, DefaultSymbolAnimationSpeed },  // A
        { 6, DefaultSymbolAnimationSpeed },  // K
        { 7, DefaultSymbolAnimationSpeed },  // Q
        { 8, DefaultSymbolAnimationSpeed },  // J
        { 9, DefaultSymbolAnimationSpeed },  // "10"
        {10, DefaultSymbolAnimationSpeed },  // "9"
        {11, DefaultSymbolAnimationSpeed },  // Wild
        {12, DefaultSymbolAnimationSpeed }   // BlueOrb
    };

    // Internal array built from named sprites
    private Sprite[] symbolSprites;

    [Header("Win Animation Sprite Arrays")]
    [Tooltip("Optional per-symbol win-animation frame sequences. Leave any empty until real art exists — animation playback already no-ops safely on an empty list.")]
    [SerializeField] private List<Sprite> animSpritesFairy;          // ID: 0
    [SerializeField] private List<Sprite> animSpritesUnicorn;        // ID: 1
    [SerializeField] private List<Sprite> animSpritesSquirrel;       // ID: 2
    [SerializeField] private List<Sprite> animSpritesMushrooms;      // ID: 3
    [SerializeField] private List<Sprite> animSpritesLotus;          // ID: 4
    [SerializeField] private List<Sprite> animSpritesA;              // ID: 5
    [SerializeField] private List<Sprite> animSpritesK;              // ID: 6
    [SerializeField] private List<Sprite> animSpritesQ;              // ID: 7
    [SerializeField] private List<Sprite> animSpritesJ;              // ID: 8
    [SerializeField] private List<Sprite> animSpritesTen;            // ID: 9  (named "10")
    [SerializeField] private List<Sprite> animSpritesNine;           // ID: 10 (named "9")
    [SerializeField] private List<Sprite> animSpritesWild;           // ID: 11
    [SerializeField] private List<Sprite> animSpritesBlueOrb;        // ID: 12

    // Internal array of animation sprite lists
    private List<Sprite>[] animationSpriteArrays;

    [Header("Win Animation Spine Assets")]
    [Tooltip("Optional per-symbol Spine win animation (the _SkeletonData asset). A symbol with one plays it on its win-layer cell; one without falls back to its frame list above, then to its static sprite.")]
    [SerializeField] private SkeletonDataAsset spineFairy;           // ID: 0
    [SerializeField] private SkeletonDataAsset spineUnicorn;         // ID: 1
    [SerializeField] private SkeletonDataAsset spineSquirrel;        // ID: 2
    [SerializeField] private SkeletonDataAsset spineMushrooms;       // ID: 3
    [SerializeField] private SkeletonDataAsset spineLotus;           // ID: 4
    [SerializeField] private SkeletonDataAsset spineA;               // ID: 5
    [SerializeField] private SkeletonDataAsset spineK;               // ID: 6
    [SerializeField] private SkeletonDataAsset spineQ;               // ID: 7
    [SerializeField] private SkeletonDataAsset spineJ;               // ID: 8
    [SerializeField] private SkeletonDataAsset spineTen;             // ID: 9  (named "10")
    [SerializeField] private SkeletonDataAsset spineNine;            // ID: 10 (named "9")
    [SerializeField] private SkeletonDataAsset spineWild;            // ID: 11
    [SerializeField] private SkeletonDataAsset spineBlueOrb;         // ID: 12

    // Internal array of Spine assets, indexed by symbol id
    private SkeletonDataAsset[] spineAssets;

    [Header("Bonus Orb Spine Assets")]
    [Tooltip("The Purple (magenta) and Yellow bonus orbs' Spine animations. Not reel symbols — the reels only ever show the Blue orb (spineBlueOrb above) — so these are not in the symbol-id table and nothing in the win presentation plays them. Read through PurpleOrbSpine / YellowOrbSpine.")]
    [SerializeField] private SkeletonDataAsset spinePurpleOrb;
    [SerializeField] private SkeletonDataAsset spineYellowOrb;

    internal SkeletonDataAsset PurpleOrbSpine => spinePurpleOrb;
    internal SkeletonDataAsset YellowOrbSpine => spineYellowOrb;

    [Header("Reel Containers")]
    [SerializeField] private Transform[] reelTransforms;

    [Header("Reel Images")]
    [SerializeField] private List<ReelImages> reelImagesList;

    // ── Symbol sizing / reel pitch ──────────────────────────────────────────────────────────────
    // Not serialized, so the code is the single source of truth — a serialized value would be
    // overridden by whatever the scene saved. The trade is that changing them needs a recompile.

    // Rect size used by every symbol not listed in SymbolSizeOverrides.
    private Vector2 normalSymbolSize = new Vector2(175f, 175f);

    // Must match the actual icon pitch in the scene. Drives the spin loop's travel distance, which
    // has to be a whole number of pitches or the loop's wrap-around is visible.
    private float symbolHeight = 245f;

    [Header("Spin Settings")]
    [SerializeField] private float spinSpeed = 6000f;
    [SerializeField] private float reelStartStagger = 0.08f;
    [SerializeField] private float reelStopStagger = 0.12f;

    [Header("Animation Settings - Casino Style")]
    [SerializeField] private float anticipationUpDistance = 20f;
    [SerializeField] private float anticipationUpDuration = 0.12f;

    [Header("Stop Animation Settings")]
    // One continuous tween using DOTween's overshoot-and-settle curve for the landing.
    [SerializeField] private Ease stopEase = Ease.OutBack;
    [Tooltip("Overshoot strength for stopEase — how far a reel bounces past its landing before it settles.")]
    [SerializeField] private float stopEaseOvershoot = 0.9f;
    [Tooltip("Length of the landing tween. Authored rather than derived from distance and spin speed, like every other stop timing in this file.")]
    [SerializeField] private float stopDuration = 0.5f;

    [Header("Quick Spin Settings")]
    [SerializeField] private float quickStopStagger = 0.06f;
    [SerializeField] private float quickStopOvershoot = 20f;
    [SerializeField] private float quickStopDuration = 0.2f;

    [Header("Scatter Anticipation")]
    [Tooltip("Effects shown on a reel held for a possible BlueOrb trigger. Index 0 = reel 2, index 1 = reel 3, index 2 = reel 4, index 3 = reel 5. Reel 1 can never anticipate, since a near miss needs earlier reels to have landed. Each plays its own frames: give its ImageAnimation the clip, with Start On Enable and looping on.")]
    [SerializeField] private GameObject[] anticipationEffects = new GameObject[4];
    [Tooltip("Extra time each held reel keeps spinning. Applied per held reel and cumulative, so several holds in one spin add up.")]
    [SerializeField] private float anticipationExtraTime = 2f;
    [Tooltip("Shorter hold used when the player is on Turbo.")]
    [SerializeField] private float anticipationExtraTimeTurbo = 1f;

    // Added to the computed stop time before a stop sequence stops waiting for its reels to report
    // they landed. Only reached if a landing tween is killed before it completes; the round then
    // carries on rather than stalling.
    private const float LandingTimeoutMargin = 2f;

    [Header("Continuous Spin (Tween) Settings")]
    [Tooltip("Filler image slots prepended above the visible window, giving the continuous spin loop room to travel before it has to wrap.")]
    [SerializeField] private int bufferRowsAbove = 16;
    [SerializeField] private Ease spinLoopEase = Ease.Linear;


    [Header("Win Animation Settings")]
    [SerializeField] private float winSymbolLoopDuration = 1.5f;

    // The shape of the win presentation: every winning symbol plays twice in step (autoplay and free
    // spins stop there; no board total since 2026-11-18). A manual spin then walks the winning lines,
    // each playing its own cells winLineRounds times, WalkPasses times over, and ends with every
    // winning symbol looping — see PlayWinLineCycleRoutine. Each round lasts as long as the
    // slowest winning symbol's clip; a win whose symbols have no clip holds once for
    // winSymbolLoopDuration instead (owner, 2026-11-16, when the win boxes went).
    private const int totalWinRounds = 2;
    private const int winLineRounds = 2;

    // How many times the walk goes through every winning line before the closing total (owner,
    // 2026-11-10).
    private const int WalkPasses = 2;

    // The per-line amounts' entrance (owner, 2026-11-04; first made for the board total, which no
    // longer shows): from its scene scale up to this many times it, then settling to the second and
    // staying there. Each half this long.
    private const float TotalWinPopScale = 1.7f;
    private const float TotalWinSettleScale = 1.3f;
    private const float TotalWinPopUpDuration = 0.15f;
    private const float TotalWinPopSettleDuration = 0.10f;
    [Tooltip("Delay between raising the win animation layer and starting the ImageAnimation - for sync timing")]
    [SerializeField] private float winLineBoxToAnimationDelay = 0.05f;

    [Header("Win Presentation Layer")]
    [Tooltip("Dark sheet covering the reel area during a win. Snaps on/off, no fade.")]
    [SerializeField] private GameObject winDimOverlay;
    [Tooltip("Root of the layer holding the bright winning symbols, drawn above the dim.")]
    [SerializeField] private GameObject winAnimationLayer;
    [Tooltip("One entry per reel column, each holding the 3 active-row slots top to bottom.")]
    [SerializeField] private List<AnimSlotColumn> animSlotColumns = new List<AnimSlotColumn>(3);

    [Tooltip("The per-line win amount, ONE PER ROW, top to bottom — element 0 is the top row. This game draws no payline graphics: a win is shown by animating its symbols and putting its payout on the middle reel, so only three positions are ever needed.")]
    [SerializeField] private TMPro.TMP_Text[] winLineAmounts = new TMPro.TMP_Text[3];

    [Tooltip("The payline drawn during the line walk: ONE Image over the reels (Raycast Target off, saved off), its sprite swapped to the winning line's. Off between lines and outside the walk. Empty: the walk shows no line.")]
    [SerializeField] private Image paylineImage;
    [Tooltip("One sprite per payline, in the server's line order — element 0 is line 0; 20 for Wild Jokers. A line with no sprite here shows no image.")]
    [SerializeField] private List<Sprite> paylineSprites = new List<Sprite>();

    [Header("Phase 1 Total Win Presentation (not shown)")]
    [Tooltip("SpinWinText — the board total. Never shown since 2026-11-18 (owner); only kept off.")]
    [SerializeField] private TMPro.TMP_Text phase1TotalWinText;

    [Tooltip("SpinWinAnimation — the burst behind the total, the total's parent. Never switched on since 2026-11-18 (owner); only kept off.")]
    [SerializeField] private ImageAnimation totalWinBurst;

    [Header("Symbol Info Card")]
    [SerializeField] private SymbolInfoCard symbolInfoCard;


    private float middlePosition = 0f;


    private List<Tween> spinTweens = new List<Tween>();
    private List<Tween> winTweens = new List<Tween>();
    private Coroutine winAnimationCoroutine;

    // Set when the controller asks for the line walk (PlayWinLineCycle) while a presentation is still
    // on its total, which decided at its start whether to walk the lines. Honoured when the total
    // ends; reset at the start of every presentation.
    private bool lineWalkRequested;

    // The lines from the spin that just landed, kept so the controller can start the Phase 2 cycle
    // after the fact — autoplay and free spins skip it while they run, and only the controller knows
    // when the round is actually over. Null on a losing spin: StartSpin clears it, and only a win
    // writes it again.
    private List<WinLine> lastWinLines;
    // The board total for lastWinLines: the server's grandTotalWin, kept for PlayWinLineCycle, which
    // replays the presentation after the controller has already let go of that spin's result.
    private double lastWinTotal;

    // Which reels are being held back to tease a trigger this spin.
    // Filled before the reels start stopping; StopSingleReel raises and clears each effect off its
    // own landing events. More than one reel can be held in a single spin — see
    // ComputeAnticipatedReels.
    private readonly HashSet<int> anticipatedReels = new HashSet<int>();

    // A per-spin claim on the win dim: true while something raised it before the win presentation
    // and means the presentation to inherit it, instead of dropping and re-raising it (which
    // flickers). Nothing sets it today; kept, with its guards, as the pattern to follow.
    private bool dimHeld;

    // A feature round's claim on the shared dim, alongside dimHeld above. A round that owns the
    // board holds this for its whole duration, so an ordinary win teardown inside the round cannot
    // drop the dim out from under it. Nothing sets it today; kept like dimHeld.
    private bool featureDimHeld;

    // Ids the scroll buffer is allowed to pick from, one list per reel. Built once and cached, since
    // gameConfig doesn't change after init and this is read on every buffer icon of every spin. See
    // EnsureFillerSymbolIds for what would justify excluding a symbol from it.
    private List<int>[] fillerSymbolIdsByReel;
    // The free games' pools, built alongside, and which set is in use.
    private List<int>[] freeGamesFillerSymbolIdsByReel;
    private bool useFreeGamesFiller;


    internal List<List<int>> currentDisplayMatrix;

    private bool isSpinning;

    // Config-driven, not Inspector-array-length-driven: reelTransforms/reelImagesList may still
    // have leftover unused slots from an older game's layout, so this must
    // reflect the real backend's reel count, not the serialized array size.
    internal int ReelCount => (gameManager != null && gameManager.gameConfig != null)
        ? gameManager.gameConfig.reelCount
        : (reelTransforms != null ? reelTransforms.Length : 3);

    // Row count (3). Every row the server sends is live and pays, so a row index means the same thing
    // in the server payload, in currentDisplayMatrix, and in each reel's displayImages list.
    internal int RowCount => (gameManager != null && gameManager.gameConfig != null) ? gameManager.gameConfig.rowCount : 3;

    // Every symbol the init's symbol table puts in the scatter group — the BlueOrb in Aladdin's
    // Treasure. Read from the table's group, so it follows the init.
    private bool IsScatterSymbol(int symbolId)
    {
        var symbols = gameManager?.gameConfig?.symbols;
        if (symbols == null) return false;

        SymbolInfo symbol = symbols.Find(s => s.id == symbolId);
        return symbol != null && symbol.isScatter;
    }

    #region Initialization

    
    private void Awake()
    {
        BuildSymbolSpriteArray();
        InitializeReels();
    }

    private void Start()
    {
        if (symbolSprites == null || symbolSprites.Length == 0)
        {
            BuildSymbolSpriteArray();
        }
        DisableAllOverlays();
        SetupSymbolButtons();
    }

    private void DisableAllOverlays()
    {
        HidePhase1TotalWinText();
        HideAnticipationEffects();
        HideWinSlots();
        HideAllWinLines();
        // Release rather than Hide: this is the full teardown, so no claim on the dim survives it.
        ReleaseHeldDim();
        HideWinDim();
        if (symbolInfoCard) symbolInfoCard.HideCard();
    }

    private void HideAnticipationEffects()
    {
        anticipatedReels.Clear();
        AudioManager.Instance?.StopAnticipation();
        if (anticipationEffects == null) return;
        foreach (var effect in anticipationEffects)
        {
            if (effect != null) effect.SetActive(false);
        }
    }

    private void SetupSymbolButtons()
    {
        if (reelImagesList == null) return;
        for (int col = 0; col < reelImagesList.Count; col++)
        {
            var reel = reelImagesList[col];
            if (reel == null || reel.displayImages == null) continue;
            int rowCount = RowCount;
            for (int row = 0; row < rowCount; row++)
            {
                if (row < reel.displayImages.Count && reel.displayImages[row] != null)
                {
                    Image img = reel.displayImages[row];
                    SymbolButtonHandler btnHandler = img.GetComponent<SymbolButtonHandler>();
                    if (btnHandler == null)
                    {
                        btnHandler = img.gameObject.AddComponent<SymbolButtonHandler>();
                    }
                    btnHandler.Init(col, row, this);
                }
            }
        }
    }

    internal void OnBetChanged()
    {
        if (symbolInfoCard != null && symbolInfoCard.gameObject.activeSelf)
        {
            symbolInfoCard.RefreshCard(gameManager);
        }
    }

    internal void OnSymbolClicked(int col, int row, RectTransform symbolRect)
    {
        if (isSpinning)
        {
            if (symbolInfoCard != null) symbolInfoCard.HideCard();
            return;
        }

        int matrixRow = row;
        if (currentDisplayMatrix == null || col >= currentDisplayMatrix.Count || matrixRow < 0 || matrixRow >= currentDisplayMatrix[col].Count)
        {
            return;
        }

        int symbolId = currentDisplayMatrix[col][matrixRow];

        if (symbolInfoCard != null)
        {
            symbolInfoCard.ShowCard(symbolId, col, row, symbolRect, gameManager);
        }
    }

    private void BuildSymbolSpriteArray()
    {
        // Build the symbol sprite array from named sprite fields
        symbolSprites = new Sprite[SymbolCount];
        symbolSprites[0] = spriteFairy;
        symbolSprites[1] = spriteUnicorn;
        symbolSprites[2] = spriteSquirrel;
        symbolSprites[3] = spriteMushrooms;
        symbolSprites[4] = spriteLotus;
        symbolSprites[5] = spriteA;
        symbolSprites[6] = spriteK;
        symbolSprites[7] = spriteQ;
        symbolSprites[8] = spriteJ;
        symbolSprites[9] = spriteTen;
        symbolSprites[10] = spriteNine;
        symbolSprites[11] = spriteWild;
        symbolSprites[12] = spriteBlueOrb;

        // Validate
        for (int i = 0; i < symbolSprites.Length; i++)
        {
            if (symbolSprites[i] == null)
            {
                Debug.LogError($"[SlotView] Symbol sprite at index {i} is not assigned in inspector!");
            }
        }

        // Build the animation sprite arrays (any entry left empty simply won't animate)
        animationSpriteArrays = new List<Sprite>[SymbolCount];
        animationSpriteArrays[0] = animSpritesFairy;
        animationSpriteArrays[1] = animSpritesUnicorn;
        animationSpriteArrays[2] = animSpritesSquirrel;
        animationSpriteArrays[3] = animSpritesMushrooms;
        animationSpriteArrays[4] = animSpritesLotus;
        animationSpriteArrays[5] = animSpritesA;
        animationSpriteArrays[6] = animSpritesK;
        animationSpriteArrays[7] = animSpritesQ;
        animationSpriteArrays[8] = animSpritesJ;
        animationSpriteArrays[9] = animSpritesTen;
        animationSpriteArrays[10] = animSpritesNine;
        animationSpriteArrays[11] = animSpritesWild;
        animationSpriteArrays[12] = animSpritesBlueOrb;

        // Build the Spine asset array (any entry left empty falls back to frames, then the sprite)
        spineAssets = new SkeletonDataAsset[SymbolCount];
        spineAssets[0] = spineFairy;
        spineAssets[1] = spineUnicorn;
        spineAssets[2] = spineSquirrel;
        spineAssets[3] = spineMushrooms;
        spineAssets[4] = spineLotus;
        spineAssets[5] = spineA;
        spineAssets[6] = spineK;
        spineAssets[7] = spineQ;
        spineAssets[8] = spineJ;
        spineAssets[9] = spineTen;
        spineAssets[10] = spineNine;
        spineAssets[11] = spineWild;
        spineAssets[12] = spineBlueOrb;
    }

    private void InitializeReels()
    {
        middlePosition = -67.4f;

        int rowCount = RowCount;

        currentDisplayMatrix = new List<List<int>>();
        for (int col = 0; col < ReelCount; col++)
        {
            var defaultCol = new List<int>();
            for (int r = 0; r < rowCount; r++)
            {
                defaultCol.Add(0);
            }
            currentDisplayMatrix.Add(defaultCol);
        }
    }

    internal void SetInitialMatrix(List<List<int>> matrix)
    {
        if (matrix == null || matrix.Count != ReelCount) return;

        int rowCount = RowCount;

        for (int col = 0; col < ReelCount; col++)
        {
            if (matrix[col].Count != rowCount) return;
        }

        currentDisplayMatrix = matrix;

        for (int col = 0; col < ReelCount; col++)
        {
            SetReelSymbols(col, matrix[col], true);
        }
    }

    #endregion

    #region Symbol Display

    private void SetReelSymbols(int columnIndex, List<int> visibleSymbolIds, bool isInitial = false)
    {
        if (columnIndex >= reelImagesList.Count)
        {
            Debug.LogError($"SetReelSymbols: Invalid column index {columnIndex}, max is {reelImagesList.Count - 1}");
            return;
        }

        int rowCount = RowCount;

        if (visibleSymbolIds == null || visibleSymbolIds.Count != rowCount)
        {
            Debug.LogError($"SetReelSymbols: Invalid visibleSymbolIds count {visibleSymbolIds?.Count}, expected {rowCount}");
            return;
        }

        var reel = reelImagesList[columnIndex];

        if (reel.images == null)
        {
            Debug.LogError($"SetReelSymbols: Reel {columnIndex} has no images assigned");
            return;
        }

        WriteDisplayBlockSprites(columnIndex, visibleSymbolIds);
        RandomizeBufferSprites(columnIndex);

        if (isInitial && reelTransforms[columnIndex] != null)
        {
            reelTransforms[columnIndex].localPosition = new Vector3(
                reelTransforms[columnIndex].localPosition.x,
                middlePosition,
                0
            );
        }
    }

    // Writes only the display-block sprites (no buffer reshuffle, no position touch). Called from
    // SetReelSymbols above.
    private void WriteDisplayBlockSprites(int columnIndex, List<int> visibleSymbolIds)
    {
        if (columnIndex >= reelImagesList.Count) return;

        int rowCount = RowCount;
        if (visibleSymbolIds == null || visibleSymbolIds.Count != rowCount) return;

        var reel = reelImagesList[columnIndex];
        if (reel.displayImages == null) return;

        for (int row = 0; row < rowCount; row++)
        {
            if (row < reel.displayImages.Count && reel.displayImages[row] != null)
            {
                int symbolId = visibleSymbolIds[row];

                ApplySymbol(reel.displayImages[row], symbolId, manageRaycast: true);
            }
        }
    }

    // Falls back rather than throwing on an unlisted id: only symbols being retuned need an entry,
    // and an id with no entry is the normal case, not an error.
    private static float GetSymbolAnimationSpeed(int symbolId)
    {
        return SymbolAnimationSpeeds.TryGetValue(symbolId, out float speed)
            ? speed
            : DefaultSymbolAnimationSpeed;
    }

    private Sprite GetSymbolSprite(int symbolId)
    {
        // Validate symbolId range (0..SymbolCount-1)
        if (symbolId < 0 || symbolId >= symbolSprites.Length)
        {
            Debug.LogWarning($"[SlotView] Invalid symbolId {symbolId}, using default sprite 0. Total sprites: {symbolSprites.Length}");
            return symbolSprites[0];
        }

        if (symbolSprites[symbolId] == null)
        {
            Debug.LogError($"[SlotView] Symbol sprite for ID {symbolId} is null!");
            return symbolSprites[0];
        }

        return symbolSprites[symbolId];
    }

    // Single place that puts a symbol onto an icon. Sprite and size are set together on purpose:
    // symbols can be drawn at different scales (see SymbolSizeOverrides), so each needs its own
    // rect. Because every write goes through here and always sets a size, an icon that showed an
    // oversized symbol is snapped back as soon as it's given any other — no reset pass to maintain
    // and no way for an icon to get stuck oversized.
    private void ApplySymbol(Image image, int symbolId, bool manageRaycast = false)
    {
        if (image == null) return;

        image.sprite = GetSymbolSprite(symbolId);

        // Sizing is art-driven, not role-driven, which is why this reads an id-keyed map. Anything
        // above the 245 pitch overlaps its vertical neighbours, which is intentional art bleed, but also means
        // those symbols swallow clicks aimed at the ones above and below them.
        image.rectTransform.sizeDelta = SymbolSizeOverrides.TryGetValue(symbolId, out Vector2 size)
            ? size
            : normalSymbolSize;

        // Display icons must catch clicks so the symbol info card can open on them. This is set
        // here rather than left to the scene so it can't be lost by an icon being re-authored.
        //
        // Opt-in rather than unconditional, because the other two callers must not get it: the
        // win-animation layer's slots are authored raycast-off and have to stay that way (they sit
        // above the reels during a win), and the scroll buffer has no info card to open.
        //
        // Oversized symbols (see the size table) overlap their neighbours, so they can steal clicks
        // aimed at the cells above and below.
        if (manageRaycast)
        {
            image.raycastTarget = true;
        }
    }

    // Randomizes the pure spin-loop scroll buffer. images now holds only buffer icons (the 5
    // real display-block icons live in displayImages instead), so no start/end boundary math
    // is needed — every entry here is fair game for random filler.
    private void RandomizeBufferSprites(int columnIndex)
    {
        if (columnIndex >= reelImagesList.Count) return;
        var reel = reelImagesList[columnIndex];
        if (reel.images == null) return;

        List<int> fillerSymbolIds = GetFillerSymbolIds(columnIndex);

        for (int i = 0; i < reel.images.Count; i++)
        {
            // Held in a variable so ApplySymbol can size it — the pool spans every symbol id, so a
            // special that needs its own rect size resizes as it scrolls past just like a landed one.
            int symbolId = fillerSymbolIds.Count > 0
                ? fillerSymbolIds[Random.Range(0, fillerSymbolIds.Count)]
                : 0;
            ApplySymbol(reel.images[i], symbolId);
        }
    }

    // The filler pool for one reel. Before init there is no config and so no per-reel rule: every id
    // is allowed, and nothing is cached, so the real pools are built once the config arrives.
    private List<int> GetFillerSymbolIds(int columnIndex)
    {
        EnsureFillerSymbolIds();

        List<int>[] pools = useFreeGamesFiller ? freeGamesFillerSymbolIdsByReel : fillerSymbolIdsByReel;
        if (pools != null && columnIndex >= 0 && columnIndex < pools.Length)
        {
            return pools[columnIndex];
        }

        return BuildFillerSymbolIds(columnIndex, null, false);
    }

    /// <summary>
    /// Switches the scrolling filler between the base game's pools and the free games' (the Wild on
    /// reels 2-5, no "10" or "9" — ATFreeGamesBonus.md §1.6). The controller switches it while the
    /// transition covers the screen.
    /// </summary>
    internal void SetFreeGamesFiller(bool on)
    {
        useFreeGamesFiller = on;
    }

    // Builds the per-reel filler pools once and reuses them — gameConfig is fixed for the session,
    // and this is read on every buffer icon of every spin.
    //
    // Every id is in every pool, except that the Wild only scrolls past on the reels it can land on
    // (features.wild.mainGameReels in the base game, freeGameReels in the free games), and the free
    // games leave out the symbols they never show (GameConfig.freeGamesExcludedSymbolIds). Exclude a
    // symbol here if it should never appear as random filler — anything the backend has to PLACE
    // deliberately. Seeing one scroll past would be showing something the server never sent.
    private void EnsureFillerSymbolIds()
    {
        if (fillerSymbolIdsByReel != null) return;

        GameConfig config = gameManager != null ? gameManager.gameConfig : null;
        if (config == null) return;

        fillerSymbolIdsByReel = new List<int>[ReelCount];
        freeGamesFillerSymbolIdsByReel = new List<int>[ReelCount];
        for (int col = 0; col < fillerSymbolIdsByReel.Length; col++)
        {
            fillerSymbolIdsByReel[col] = BuildFillerSymbolIds(col, config, false);
            freeGamesFillerSymbolIdsByReel[col] = BuildFillerSymbolIds(col, config, true);
        }
    }

    private static List<int> BuildFillerSymbolIds(int columnIndex, GameConfig config, bool freeGames)
    {
        int wildId = config != null ? config.wildSymbolId : -1;
        List<int> wildReels = config == null ? null : (freeGames ? config.wildFreeGameReels : config.wildMainGameReels);
        // No reel list sent means no known rule, so the Wild is left on every reel rather than none.
        bool wildAllowed = wildId < 0
            || wildReels == null
            || wildReels.Count == 0
            || wildReels.Contains(columnIndex);

        List<int> excluded = freeGames && config != null ? config.freeGamesExcludedSymbolIds : null;

        var ids = new List<int>(SymbolCount);
        for (int id = 0; id < SymbolCount; id++)
        {
            if (id == wildId && !wildAllowed) continue;
            if (excluded != null && excluded.Contains(id)) continue;
            ids.Add(id);
        }
        return ids;
    }

    #endregion

    #region Spin Animation

    // together: every reel starts at once (the free games, ATFreeGamesBonus.md §5.2) instead of one
    // after another. Landing is unaffected — the reels still land left to right.
    internal void StartSpin(bool together = false)
    {
        if (isSpinning) return;

        if (symbolInfoCard != null) symbolInfoCard.HideCard();

        isSpinning = true;

        // Cleared at every spin start: only a winning spin writes it, so a losing spin must not leave
        // the previous win for PlayWinLineCycle to replay over the new board.
        lastWinLines = null;

        KillAllTweens();

        DisableAllOverlays();

        for (int col = 0; col < ReelCount; col++)
        {
            RandomizeBufferSprites(col);
            StartReelCycleWithDelay(col, together ? 0f : col * reelStartStagger);
        }
    }

    private void StartReelCycleWithDelay(int columnIndex, float delay)
    {
        if (columnIndex >= reelTransforms.Length) return;

        Transform slotTransform = reelTransforms[columnIndex];

        Sequence startSequence = DOTween.Sequence();

        if (delay > 0)
        {
            startSequence.AppendInterval(delay);
        }

        startSequence.Append(
            slotTransform.DOLocalMoveY(middlePosition + anticipationUpDistance, anticipationUpDuration)
                .SetEase(Ease.OutQuad)
        );

        startSequence.Append(
            slotTransform.DOLocalMoveY(middlePosition, anticipationUpDuration * 0.5f)
                .SetEase(Ease.InQuad)
        );

        startSequence.OnComplete(() => {
            if (isSpinning)
            {
                StartContinuousLoop(columnIndex);
            }
        });

        startSequence.Play();

        if (spinTweens.Count <= columnIndex)
            spinTweens.Add(startSequence);
        else
            spinTweens[columnIndex] = startSequence;
    }

    // One continuous loop tween per column. The strip's sprites are set once at StartSpin() and left
    // alone for the rest of the spin — reshuffling on each wrap shows as symbols popping mid-scroll.
    private void StartContinuousLoop(int columnIndex)
    {
        if (columnIndex >= reelTransforms.Length) return;
        if (!isSpinning) return;

        Transform slotTransform = reelTransforms[columnIndex];

        slotTransform.localPosition = new Vector3(slotTransform.localPosition.x, middlePosition, 0);

        float loopDistance = bufferRowsAbove * symbolHeight;
        float loopDuration = loopDistance / spinSpeed;

        Tween loopTween = slotTransform.DOLocalMoveY(middlePosition - loopDistance, loopDuration)
            .SetEase(spinLoopEase)
            .SetLoops(-1, LoopType.Restart);

        loopTween.Play();

        if (spinTweens.Count <= columnIndex)
            spinTweens.Add(loopTween);
        else
            spinTweens[columnIndex] = loopTween;
    }

    #endregion

    #region Stop Spin

    internal void StopSpin(List<List<int>> resultMatrix, System.Action onComplete)
    {
        if (!isSpinning)
        {
            currentDisplayMatrix = resultMatrix;
            for (int col = 0; col < ReelCount; col++)
            {
                SetReelSymbols(col, resultMatrix[col], false);
            }
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(StopSpinSequence(resultMatrix, onComplete, false));
    }

    private IEnumerator StopSpinSequence(List<List<int>> resultMatrix, System.Action onComplete, bool isQuickStop)
    {
        currentDisplayMatrix = resultMatrix;

        // Anything per-cell that a reel's landing write needs from the spin result has to be
        // captured here, before any reel lands, and never read at DRAW time: the last reel lands on
        // the same frame this sequence reports completion, and the controller nulls lastResult a
        // frame or two later, so a draw-time read is a race the last reel routinely loses. (Gold of
        // Luck captured its Genie multipliers here for exactly that reason.)

        // GameManager.GetSpinDuration() already enforces the minimum spin time before this is
        // ever called, so there's no need for a separate discrete-cycle-count gate here.
        float stagger = isQuickStop ? quickStopStagger : reelStopStagger;

        // Skipped entirely on a quick stop — that path covers both QuickSpin mode and the player
        // hitting Stop, and neither should sit through the hold. StopSingleReel reads this set to
        // know when to raise and clear each effect.
        anticipatedReels.Clear();
        if (!isQuickStop) ComputeAnticipatedReels(resultMatrix, anticipatedReels);
        float anticipationHold = GetAnticipationHold();

        // Each held reel spins on for anticipationHold, which pushes itself and everything after
        // it back by that much — so the delays are cumulative and the reels still land left to
        // right. Four held reels really do add four holds; that is the intended drama.
        //
        // Completion is reported by the reels themselves: each one's landing tween counts in when it
        // finishes, and the hand-off waits for all of them — never for a duration computed here.
        int reelsToLand = 0;
        int reelsLanded = 0;
        int holdsSoFar = 0;
        for (int col = 0; col < ReelCount; col++)
        {
            if (anticipatedReels.Contains(col)) holdsSoFar++;

            float delay = col * stagger + anticipationHold * holdsSoFar;
            reelsToLand++;
            StartCoroutine(StopSingleReel(col, resultMatrix[col], delay, isQuickStop, () => reelsLanded++));
        }

        // A safety net only, so a landing tween killed before it completes can never stall the round:
        // the expected stop time plus a margin. In the normal case every reel reports long before.
        float expectedStopTime = (ReelCount - 1) * stagger
            + (isQuickStop ? quickStopDuration : stopDuration)
            + anticipationHold * anticipatedReels.Count;
        float giveUpAt = Time.time + expectedStopTime + LandingTimeoutMargin;

        yield return new WaitUntil(() => reelsLanded >= reelsToLand || Time.time >= giveUpAt);
        if (reelsLanded < reelsToLand)
        {
            Debug.LogWarning($"[SlotView] Only {reelsLanded} of {reelsToLand} reels reported landing — carrying on.");
        }

        isSpinning = false;

        // Cut the spin loop here rather than in the controller's OnReelsStoppedComplete: this is the
        // real moment the last reel lands, on both the normal and the quick stop. A teased reel keeps
        // the loop running while it spins on, since this waits for it.
        AudioManager.Instance?.StopSpinLoop();

        // Safety net only — the tension normally stops as the last held reel lands, below.
        AudioManager.Instance?.StopAnticipation();

        onComplete?.Invoke();
    }

    /// <summary>
    /// Which reels are held back to tease a trigger — the BlueOrb rule (ATFreeGamesBonus.md §3.1), in
    /// the base game and the free games alike (where a third orb is a retrigger). Orbs are counted by
    /// cell, so two on one reel count as two. Once two have landed, every reel after the one holding
    /// the second is held, one after another as they land — until a reel lands the third, which ends
    /// it: the reels after that land normally. A quick stop never calls this.
    /// </summary>
    private void ComputeAnticipatedReels(List<List<int>> resultMatrix, HashSet<int> results)
    {
        GameConfig config = gameManager != null ? gameManager.gameConfig : null;
        if (resultMatrix == null || config == null) return;

        int orbId = config.freeSpinsSymbolId;
        if (orbId < 0) return;

        int trigger = config.freeSpinsMinTrigger > 0 ? config.freeSpinsMinTrigger : 3;
        int landedSoFar = 0;

        for (int col = 0; col < ReelCount; col++)
        {
            // Two already down and the trigger not yet complete: this reel is held.
            if (landedSoFar >= trigger - 1 && landedSoFar < trigger) results.Add(col);

            landedSoFar += CountSymbolInColumn(resultMatrix, col, orbId);

            // The trigger is complete once this reel lands: nothing after it is held.
            if (landedSoFar >= trigger) break;
        }
    }

    // How many visible cells of one reel show the symbol — counted by cell, so two on one reel count
    // as two.
    private int CountSymbolInColumn(List<List<int>> matrix, int col, int symbolId)
    {
        if (symbolId < 0 || matrix == null || col < 0 || col >= matrix.Count || matrix[col] == null) return 0;

        int count = 0;
        int rowEnd = Mathf.Min(RowCount, matrix[col].Count);
        for (int row = 0; row < rowEnd; row++)
        {
            if (matrix[col][row] == symbolId) count++;
        }
        return count;
    }

    private float GetAnticipationHold()
    {
        bool isTurbo = gameManager != null && gameManager.currentSpinSpeed == SpinSpeed.Turbo;
        return isTurbo ? anticipationExtraTimeTurbo : anticipationExtraTime;
    }

    // effects[0] belongs to reel index 1, effects[1] to reel index 2, and so on — reel 0 can never
    // anticipate, since a near miss needs earlier reels to have landed. Each effect plays its own
    // frames: its ImageAnimation's Start On Enable starts the clip when it is switched on.
    private void SetAnticipationEffect(int reelIndex, bool visible)
    {
        int effectIndex = reelIndex - 1;
        if (anticipationEffects == null || effectIndex < 0 || effectIndex >= anticipationEffects.Length) return;

        GameObject effect = anticipationEffects[effectIndex];
        if (effect != null) effect.SetActive(visible);
    }

    // onLanded fires once the reel's landing tween has finished — the moment it is really at rest.
    private IEnumerator StopSingleReel(int columnIndex, List<int> targetSymbols, float delay, bool isQuickStop, System.Action onLanded = null)
    {
        if (delay > 0)
        {
            yield return new WaitForSeconds(delay);
        }

        if (columnIndex < spinTweens.Count && spinTweens[columnIndex] != null)
        {
            spinTweens[columnIndex].Kill();
        }

        Transform slotTransform = reelTransforms[columnIndex];

        SetReelSymbols(columnIndex, targetSymbols, false);

        // Snap to a fixed pre-land reference point so the overshoot/settle distance below is
        // consistent regardless of where in its continuous loop the reel was stopped.
        slotTransform.localPosition = new Vector3(
            slotTransform.localPosition.x,
            middlePosition + symbolHeight,
            0
        );

        // No reel-stop or scatter-landing sound: the old games' clips are gone and Wild Jokers has none
        // yet (owner, 2026-11-04).

        // The reel is at rest. Only one anticipation effect is ever up: if this reel was being teased,
        // its effect goes (whether or not the orb turned up) before the next held reel's comes in, so
        // the two never overlap. Driven off the actual landing rather than a computed timestamp so it
        // can't drift out of sync with the staggers or the holds.
        void OnReelLanded()
        {
            if (anticipatedReels.Remove(columnIndex)) SetAnticipationEffect(columnIndex, false);

            if (anticipatedReels.Contains(columnIndex + 1))
            {
                SetAnticipationEffect(columnIndex + 1, true);

                // Starts with the first held reel and runs across any that follow it.
                AudioManager.Instance?.PlayAnticipation();
            }
            else if (anticipatedReels.Count == 0)
            {
                // The last held reel has landed: the tension is over.
                AudioManager.Instance?.StopAnticipation();
            }

            onLanded?.Invoke();
        }

        if (isQuickStop)
        {
            Sequence quickStopSequence = DOTween.Sequence();

            quickStopSequence.Append(
                slotTransform.DOLocalMoveY(middlePosition - quickStopOvershoot, quickStopDuration * 0.3f)
                    .SetEase(Ease.OutQuad)
            );

            quickStopSequence.Append(
                slotTransform.DOLocalMoveY(middlePosition, quickStopDuration * 0.7f)
                    .SetEase(Ease.InOutQuad)
            );

            quickStopSequence.OnComplete(OnReelLanded);

            spinTweens[columnIndex] = quickStopSequence;
        }
        else
        {
            // Single tween — the ease's own overshoot-and-settle does the bounce.
            Tween stopTween = slotTransform.DOLocalMoveY(middlePosition, stopDuration)
                .SetEase(stopEase, stopEaseOvershoot)
                .OnComplete(OnReelLanded);

            spinTweens[columnIndex] = stopTween;
        }
    }

    #endregion

    #region Quick Spin

    internal void QuickStop(List<List<int>> resultMatrix, System.Action onComplete = null)
    {
        if (!isSpinning)
        {
            currentDisplayMatrix = resultMatrix;
            for (int col = 0; col < ReelCount; col++)
            {
                if (col < reelTransforms.Length)
                {
                    SetReelSymbols(col, resultMatrix[col], false);
                    reelTransforms[col].localPosition = new Vector3(
                        reelTransforms[col].localPosition.x,
                        middlePosition,
                        0
                    );
                }
            }

            onComplete?.Invoke();
            return;
        }

        StartCoroutine(StopSpinSequence(resultMatrix, onComplete, true));
    }

    #endregion

    #region Feature Trigger

    /// <summary>
    /// A free-games trigger or retrigger (ATFreeGamesBonus.md §3.2, §5.5): every cell showing symbolId
    /// (the BlueOrb) plays its clip on a loop — no dim (owner, 2026-11-15). It runs until
    /// ClearWinPresentation or the next spin's teardown.
    /// </summary>
    internal void LoopSymbolCells(int symbolId)
    {
        KillWinTweens();

        // A trigger is not a line win: nothing for the controller to replay as one later.
        lastWinLines = null;

        if (symbolId < 0 || currentDisplayMatrix == null) return;

        var cells = new List<int>();
        int rowCount = RowCount;
        for (int col = 0; col < ReelCount && col < currentDisplayMatrix.Count; col++)
        {
            var column = currentDisplayMatrix[col];
            if (column == null) continue;

            for (int row = 0; row < rowCount && row < column.Count; row++)
            {
                if (column[row] == symbolId) cells.Add(row * ReelCount + col);
            }
        }

        if (cells.Count == 0) return;
        winAnimationCoroutine = StartCoroutine(LoopCells(cells));
    }

    private IEnumerator LoopCells(List<int> cells)
    {
        // rounds 0 is the closing hold: everything loops, and nothing waits on it.
        yield return AnimateWinPositions(cells, rounds: 0, includeScatters: true, raiseDim: false);
        winAnimationCoroutine = null;
    }

    /// <summary>
    /// The end phase (ATFreeGamesBonus.md §6.1): the reel symbol at each of these cells (flat indices)
    /// goes, so the end orb shows on an empty position. Call after ClearWinPresentation — any win
    /// teardown puts every icon back. RestoreBoard brings them back for the base game.
    /// </summary>
    internal void SetCellsHidden(IEnumerable<int> cells)
    {
        if (cells == null) return;
        foreach (int cell in cells)
        {
            SetDisplayIconActive(cell % ReelCount, cell / ReelCount, false);
        }
    }

    /// <summary>
    /// Puts a board back on the reels — the triggering board, after the free games (§8) — with every
    /// reel icon showing again and any presentation taken down.
    /// </summary>
    internal void RestoreBoard(List<List<int>> matrix)
    {
        lastWinLines = null;
        KillWinTweens();
        SetInitialMatrix(matrix);
    }

    /// <summary>Takes any win or trigger presentation down — for a feature that ends without a spin.</summary>
    internal void ClearWinPresentation()
    {
        lastWinLines = null;
        KillWinTweens();
    }

    #endregion


    #region Win Line Animation

    // totalWin is the server's grandTotalWin, shown as the board total. It is passed in, never summed
    // from the wins here, so the board can't disagree with the win box.
    internal void ShowWinLineAnimation(List<WinLine> winLines, double totalWin, System.Action onComplete)
    {
        if (winLines == null || winLines.Count == 0)
        {
            lastWinLines = null;
            lastWinTotal = 0;
            // No win presentation is coming to inherit the dim, so anything that raised it earlier
            // in the spin has to let it go here — otherwise the board stays dark until the next spin.
            ReleaseHeldDim();
            onComplete?.Invoke();
            return;
        }

        lastWinLines = winLines;
        lastWinTotal = totalWin;

        KillWinTweens();
        winAnimationCoroutine = StartCoroutine(PlayTwoPhaseWinLines(winLines, totalWin, onComplete));
    }

    /// <summary>
    /// Starts the Phase 2 line-by-line cycle for the spin that just landed. Autoplay and free spins
    /// skip Phase 2 while they're running — a round ends with the presentation parked after Phase 1
    /// — so the controller calls this once the round is genuinely over. Walks the lines and ends on
    /// the closing total, same as an ordinary manual spin.
    ///
    /// If that spin's presentation is still running, the request is left for it rather than acted on
    /// here: it walks the lines itself once its total ends.
    /// </summary>
    internal void PlayWinLineCycle()
    {
        if (lastWinLines == null || lastWinLines.Count == 0) return;

        // Still presenting — the player stopped autoplay while the total was on screen. Starting a
        // second coroutine would double up and strobe the lines, and simply returning would drop the
        // request, because that presentation decided before its total to skip the walk. So it is
        // recorded instead, and PlayTwoPhaseWinLines honours it when the total ends.
        if (winAnimationCoroutine != null)
        {
            lineWalkRequested = true;
            return;
        }

        KillWinTweens();

        HashSet<int> allWinPositions = CollectWinPositions(lastWinLines);
        winAnimationCoroutine = StartCoroutine(PlayWinLineCycleRoutine(lastWinLines, allWinPositions, lastWinTotal));
    }

    private IEnumerator PlayTwoPhaseWinLines(List<WinLine> winLines, double totalWinAmount, System.Action onComplete)
    {
        // A request left for a previous presentation must not carry into this one.
        lineWalkRequested = false;

        int rowLimit = (gameManager != null && gameManager.gameConfig != null) ? gameManager.gameConfig.rowCount : 3;

        // ==========================================
        // PHASE 1: Show all winning icons at once
        // ==========================================
        // A cell shared by two wins (a Wild serving both) is in the set once, so it animates once.
        HashSet<int> allWinPositions = CollectWinPositions(winLines);

        Debug.Log($"[PlayTwoPhaseWinLines] Phase 1: Showing all {allWinPositions.Count} winning icons at once for {winLines.Count} wins, total {totalWinAmount}");

        // Decided BEFORE the total plays, because it picks the total's round count. It also skips the
        // line walk afterwards — unless the controller asks for the walk while the total is still
        // playing, which is checked once the total ends (see lineWalkRequested).
        bool skipPhase2 = gameManager != null
            && (gameManager.isInFreeSpins || gameManager.isAutoPlaying);

        // No total on the board (owner, 2026-11-18): SpinWinAnimation and its SpinWinText are never
        // switched on — the win box in the bottom bar is the only place the amount shows.
        AudioManager.Instance?.PlayWinPresentationStart();

        // Phase 1: every winning symbol, twice, in step — the same in autoplay and Free Games, which
        // end here (owner, 2026-11-06).
        //
        // Yielded directly, NOT through StartCoroutine. A nested StartCoroutine is its own coroutine:
        // stopping this one (the next spin's KillWinTweens) left it running, waiting forever for
        // loop-complete callbacks that teardown had already cleared — one stuck coroutine per
        // interrupted win. Run inline, it stops with this one.
        yield return AnimateWinPositions(allWinPositions, rounds: totalWinRounds);

        KillWinTweens(false);

        // Invoke onComplete immediately after phase 1 so game logic (Free Spins / Autoplay / Win
        // complete) can proceed while the win lines are still being walked.
        onComplete?.Invoke();

        // The round count above had to be fixed before the total played; the walk did not. If
        // autoplay was stopped while the total was on screen, the controller has asked for the walk
        // by now, and this spin gets it the same way a manual spin would.
        if (skipPhase2 && !lineWalkRequested)
        {
            // Take the presentation down on the way out: after the last autoplay spin, or at the end
            // of a free-games round, there is no next spin to clear it. The controller restarts the
            // cycle via PlayWinLineCycle when the round is genuinely over.
            winAnimationCoroutine = null;

            // Presentation is genuinely over here, so any dim still being held is released before
            // the teardown rather than surviving it.
            ReleaseHeldDim();

            KillWinTweens();
            yield break;
        }

        yield return PlayWinLineCycleRoutine(winLines, allWinPositions, totalWinAmount);
    }

    // ==========================================
    // PHASE 2: Individual Win Line presentation loop
    // ==========================================
    // Split out of PlayTwoPhaseWinLines so the controller can start it on its own once an autoplay
    // or free-games round ends. Ends on the closing total, with the winning cells left looping until
    // something kills them — normally the next StartSpin.
    private IEnumerator PlayWinLineCycleRoutine(List<WinLine> winLines, HashSet<int> allWinPositions, double totalWinAmount)
    {
        // The line walk (owner, 2026-11-05; two passes, 2026-11-10): each winning line in turn — its
        // payline image, its amount on reel 3 (popping in), and only its own cells — then
        // the next, WalkPasses times over; KillWinTweens takes each line down before the next. No
        // sound (the line-change clip was Golden Dynasty's).
        for (int pass = 0; pass < WalkPasses && winLines != null; pass++)
        {
            float passStart = Time.time;
            bool anyLineShown = false;

            foreach (var winLine in winLines)
            {
                if (winLine == null || winLine.positions == null || winLine.positions.Count == 0) continue;

                KillWinTweens(false);
                ShowWinLine(winLine);
                anyLineShown = true;

                // Inline, for the same reason as the total in PlayTwoPhaseWinLines.
                yield return AnimateWinPositions(winLine.positions, rounds: winLineRounds);
            }

            // Nothing to walk, or a pass that took no time (nothing on it could animate or wait):
            // another pass would do the same. Go straight to the closing total.
            if (!anyLineShown || Time.time <= passStart) break;
        }

        // The close: the payline and amount go, and every winning cell's symbol is left looping —
        // rounds: 0 starts them and returns at once — until the next spin's teardown stops them.
        // (Until 2026-11-18 the board total came back here with its burst; it no longer shows.)
        KillWinTweens(false);

        // Inline for the same reason as phase 1 in PlayTwoPhaseWinLines.
        yield return AnimateWinPositions(allWinPositions, rounds: 0);

        winAnimationCoroutine = null;
    }

    // Every winning cell across all the wins, each once. The total is not rebuilt here: it is the
    // server's grandTotalWin, kept in lastWinTotal.
    private static HashSet<int> CollectWinPositions(List<WinLine> winLines)
    {
        var allWinPositions = new HashSet<int>();

        if (winLines == null) return allWinPositions;

        foreach (var winLine in winLines)
        {
            if (winLine?.positions == null) continue;
            foreach (int flatIndex in winLine.positions) allWinPositions.Add(flatIndex);
        }

        return allWinPositions;
    }

    /// <param name="rounds">
    /// How many times every symbol clip here plays, IN STEP: they all start together, and the
    /// group waits for the slowest before going again. Left to their own loop counts they drift apart
    /// at once, since a clip's loop length is its frame count over its speed and no two match.
    /// Zero or less means the closing hold — start them looping and do not wait at all.
    /// </param>
    /// <param name="includeScatters">
    /// True only for a feature trigger (LoopSymbolCells), which animates the scatters themselves. A
    /// line win never does — see the guard below.
    /// </param>
    /// <param name="raiseDim">False: the board is not dimmed (the trigger's looping orbs).</param>
    private IEnumerator AnimateWinPositions(IEnumerable<int> flatPositions, int rounds = 1, bool includeScatters = false,
                                            bool raiseDim = true)
    {
        if (flatPositions == null) yield break;

        int rowLimit = (gameManager != null && gameManager.gameConfig != null) ? gameManager.gameConfig.rowCount : 3;

        List<ImageAnimation> activeAnims = new List<ImageAnimation>();
        List<SpineAnimController> activeSpines = new List<SpineAnimController>();

        bool anyShown = false;

        foreach (int flatIndex in flatPositions)
        {
            int row = flatIndex / ReelCount;
            int col = flatIndex % ReelCount;

            if (col < 0 || col >= ReelCount || row < 0 || row >= rowLimit) continue;

            // Image lookup goes to the animation layer, which holds one slot per visible cell.
            if (animSlotColumns == null || col >= animSlotColumns.Count) continue;
            var column = animSlotColumns[col];
            if (column == null || column.rows == null || row >= column.rows.Count) continue;

            AnimSlot slot = column.rows[row];
            if (slot == null || slot.image == null) continue;

            Image slotImage = slot.image;

            int matrixRow = row;
            if (col >= currentDisplayMatrix.Count || matrixRow >= currentDisplayMatrix[col].Count) continue;
            int symbolId = currentDisplayMatrix[col][matrixRow];

            // A guard: a scatter never takes part in a line win, so if the server ever lists one
            // among a win's positions it is left dimmed. Scatters get their own
            // presentation when they trigger a feature, which passes includeScatters.
            if (!includeScatters && IsScatterSymbol(symbolId)) continue;

            // Show the symbol first, unconditionally. Some symbols have no animation frames at all
            // (their anim list is left empty), and under the dim a skipped slot would leave a
            // winning symbol sitting dark while its neighbours light up.
            slotImage.DOKill();

            ApplySymbol(slotImage, symbolId);

            slotImage.transform.localScale = Vector3.one;
            Color c = slotImage.color;
            slotImage.color = new Color(c.r, c.g, c.b, 1f);
            slotImage.gameObject.SetActive(true);
            anyShown = true;

            // Take the reel icon underneath out of the picture entirely. It sits below the dim but
            // is still faintly visible through it, and an oversized neighbour can poke into this
            // cell — either way it reads as a ghost behind the bright copy. Hidden per-cell rather
            // than blanket-hiding the display block, so the scatter (skipped above) correctly stays
            // on screen and dimmed. HideWinSlots puts every icon back.
            SetDisplayIconActive(col, row, false);

            // Spine first: a symbol with a Spine asset plays it in place of its frames. It is started
            // with the rounds below, not here, so it stays in step with the frame clips.
            if (TryShowSpineSlot(slot, symbolId))
            {
                activeSpines.Add(slot.spine);
                continue;
            }

            // Animate only if this symbol actually has frames — otherwise its single image stays up
            // (the fallback, owner, 2026-11-16).
            if (symbolId < 0 || symbolId >= animationSpriteArrays.Length) continue;

            List<Sprite> animSprites = animationSpriteArrays[symbolId];

            if (animSprites == null || animSprites.Count == 0) continue;

            ImageAnimation imageAnim = slot.animation;
            if (imageAnim == null) continue;

            imageAnim.textureArray = animSprites;
            imageAnim.doLoopAnimation = true;
            // Must be set before StartAnimation() below — that is the only place ImageAnimation
            // reads it, so a later change would not take effect until the next start.
            imageAnim.AnimationSpeed = GetSymbolAnimationSpeed(symbolId);

            activeAnims.Add(imageAnim);
        }

        // Only raise the dim once something is actually on the layer — otherwise an empty or
        // fully-invalid position set would darken the reels with nothing shown on top.
        if (anyShown)
        {
            if (raiseDim && winDimOverlay != null) winDimOverlay.SetActive(true);
            if (winAnimationLayer != null) winAnimationLayer.SetActive(true);
        }

        if (winLineBoxToAnimationDelay > 0)
        {
            yield return new WaitForSeconds(winLineBoxToAnimationDelay);
        }

        // Nothing of our own to animate. Still hold the beat so the sequence keeps its pacing.
        if (activeAnims.Count == 0 && activeSpines.Count == 0)
        {
            if (rounds > 0) yield return new WaitForSeconds(winSymbolLoopDuration);
            yield break;
        }

        // The closing hold: set everything looping and leave it. Nothing waits on this — the next
        // spin's teardown is what ends it.
        if (rounds <= 0)
        {
            foreach (var anim in activeAnims)
            {
                anim.doLoopAnimation = true;
                anim.onLoopComplete = null;
                anim.StartAnimation();
            }

            foreach (var spine in activeSpines)
            {
                // Stop first: SpineAnimController ignores Play while it still thinks a clip is
                // playing, which it does after any one-shot round has ended.
                spine.Stop();
                spine.Play(true);
            }

            yield break;
        }

        for (int round = 0; round < rounds; round++)
        {
            int finished = 0;

            foreach (var anim in activeAnims)
            {
                ImageAnimation a = anim;

                // One pass each; the round below is what restarts them, together.
                a.doLoopAnimation = false;
                a.onLoopComplete = (loop) =>
                {
                    a.onLoopComplete = null;

                    // Reverts to frame 0, so a symbol that finishes early rests on its resting sprite
                    // instead of freezing on its last frame while it waits for the others.
                    a.StopAnimation();
                    finished++;
                };

                a.StartAnimation();
            }

            // SpineAnimController reports no clip end, so a Spine round is timed by its clip length
            // instead. The round waits for both: every frame clip's callback and the longest Spine clip.
            float spineWait = 0f;
            foreach (var spine in activeSpines)
            {
                // Stop first, for the same reason as the closing hold above.
                spine.Stop();
                spine.Play(false);
                spineWait = Mathf.Max(spineWait, spine.GetAnimationDuration());
            }
            float spineRoundEnd = Time.time + spineWait;

            yield return new WaitUntil(() => finished >= activeAnims.Count && Time.time >= spineRoundEnd);
        }
    }

    // Puts a symbol's Spine animation on its win-layer cell, ready for AnimateWinPositions to start:
    // the asset loaded, the Spine object on and the cell's static Image off. False — leaving the cell
    // as it was — when the cell has no Spine object, the symbol has no asset, or the asset has no
    // animation to play; the cell then falls back to frames or the static sprite. HideWinSlots undoes it.
    private bool TryShowSpineSlot(AnimSlot slot, int symbolId)
    {
        if (slot == null || slot.spine == null || slot.image == null) return false;
        if (spineAssets == null || symbolId < 0 || symbolId >= spineAssets.Length) return false;

        SkeletonDataAsset asset = spineAssets[symbolId];
        if (asset == null) return false;

        slot.spine.SetSkeletonData(asset);
        if (slot.spine.GetAnimationDuration() <= 0f) return false;

        slot.spine.gameObject.SetActive(true);
        slot.image.enabled = false;
        return true;
    }

    // Keeps the board total and its burst (SpinWinAnimation) off. Nothing shows them since 2026-11-18
    // (owner); this still runs at Start and in every teardown, so a scene saved with them on is
    // switched off.
    private void HidePhase1TotalWinText()
    {
        if (phase1TotalWinText != null)
        {
            phase1TotalWinText.transform.DOKill();
            phase1TotalWinText.transform.localScale = TotalWinSceneScale;
            phase1TotalWinText.gameObject.SetActive(false);
        }

        if (totalWinBurst != null)
        {
            totalWinBurst.onLoopComplete = null;
            totalWinBurst.StopAnimation();
            totalWinBurst.gameObject.SetActive(false);
        }
    }

    // The total's scale as saved in the scene, read the first time it is needed (at Start, before
    // anything has scaled it): a hidden total is put back to it.
    private bool totalWinSceneScaleRead;
    private Vector3 totalWinSceneScale = Vector3.one;
    private Vector3 TotalWinSceneScale
    {
        get
        {
            if (!totalWinSceneScaleRead && phase1TotalWinText != null)
            {
                totalWinSceneScale = phase1TotalWinText.transform.localScale;
                totalWinSceneScaleRead = true;
            }
            return totalWinSceneScale;
        }
    }

    // The per-line amounts' scales as saved in the scene, read together the first time one is shown
    // (before any of them has been popped): each pops relative to its own, and is put back to it when hidden.
    private Vector3[] winLineAmountSceneScales;
    private Vector3 WinLineAmountSceneScale(int row)
    {
        if (winLineAmountSceneScales == null && winLineAmounts != null)
        {
            winLineAmountSceneScales = new Vector3[winLineAmounts.Length];
            for (int i = 0; i < winLineAmounts.Length; i++)
            {
                winLineAmountSceneScales[i] = winLineAmounts[i] != null ? winLineAmounts[i].transform.localScale : Vector3.one;
            }
        }

        return winLineAmountSceneScales != null && row >= 0 && row < winLineAmountSceneScales.Length
            ? winLineAmountSceneScales[row]
            : Vector3.one;
    }

    // The per-line amounts' entrance (owner, 2026-11-04 and
    // 2026-11-10): on at its scene scale, up to TotalWinPopScale × it, settling to TotalWinSettleScale
    // × it, where it stays until hidden.
    private void AnimateAmountPop(Transform textTransform, Vector3 sceneScale)
    {
        if (textTransform == null) return;

        textTransform.DOKill();
        textTransform.localScale = sceneScale;
        textTransform.gameObject.SetActive(true);

        Sequence seq = DOTween.Sequence();
        seq.Append(textTransform.DOScale(sceneScale * TotalWinPopScale, TotalWinPopUpDuration).SetEase(Ease.OutQuad));
        seq.Append(textTransform.DOScale(sceneScale * TotalWinSettleScale, TotalWinPopSettleDuration).SetEase(Ease.InQuad));
        winTweens.Add(seq);
    }

    private void KillWinTweens(bool stopCoroutine = true)
    {
        foreach (var tween in winTweens)
        {
            tween?.Kill();
        }
        winTweens.Clear();

        if (stopCoroutine && winAnimationCoroutine != null)
        {
            StopCoroutine(winAnimationCoroutine);
            winAnimationCoroutine = null;
        }

        HidePhase1TotalWinText();

        // Stop all in-flight win animations and restore alpha for every icon — covers both the
        // buffer (images) and the display block (displayImages). ImageAnimation lives directly on
        // each display icon's SlotIcon root (sharing that same Image), so GetComponent finds it
        // there; buffer icons simply have none and are skipped.
        void RestoreImageList(List<Image> imageList)
        {
            if (imageList == null) return;
            foreach (var image in imageList)
            {
                if (image != null)
                {
                    image.DOKill();
                    image.transform.localScale = Vector3.one;
                    Color c = image.color;
                    image.color = new Color(c.r, c.g, c.b, 1f);

                    ImageAnimation imageAnim = image.GetComponent<ImageAnimation>();
                    if (imageAnim != null)
                    {
                        imageAnim.onLoopComplete = null;
                        imageAnim.StopAnimation();
                    }
                }
            }
        }

        foreach (var reel in reelImagesList)
        {
            RestoreImageList(reel.images);
            RestoreImageList(reel.displayImages);
        }

        // The win-layer slots are cleared on *every* call, including the between-cycle reset in
        // Phase 2 — each cycle shows one win line, so the previous line's symbols have to go
        // before the next line's appear. Its Image and ImageAnimation are separate explicit
        // references, so this can't reuse RestoreImageList's GetComponent-based pass.

        if (animSlotColumns != null)
        {
            foreach (var column in animSlotColumns)
            {
                if (column == null || column.rows == null) continue;
                foreach (var slot in column.rows)
                {
                    if (slot == null) continue;

                    if (slot.image != null)
                    {
                        slot.image.DOKill();
                        slot.image.transform.localScale = Vector3.one;
                        Color c = slot.image.color;
                        slot.image.color = new Color(c.r, c.g, c.b, 1f);
                    }

                    if (slot.animation != null)
                    {
                        slot.animation.onLoopComplete = null;
                        slot.animation.StopAnimation();
                    }

                    // Switched off, and the static Image put back, by HideWinSlots below.
                    if (slot.spine != null) slot.spine.Stop();
                }
            }
        }
        HideWinSlots();

        // Lines clear on every call too — Phase 2 shows one at a time, so the previous line has
        // to go before the next is raised.
        HideAllWinLines();

        // The dim itself only comes down on a full teardown. Hiding it on the between-cycle reset
        // would make it strobe once per win line.
        if (stopCoroutine) HideWinDim();
    }

    // Shows or hides the reel icon sitting behind one win-layer slot. Row indices need no
    // translation — displayImages holds one entry per visible row, in the same order as the
    // server matrix and the animation layer.
    private void SetDisplayIconActive(int col, int row, bool active)
    {
        if (reelImagesList == null || col < 0 || col >= reelImagesList.Count) return;

        var reel = reelImagesList[col];
        if (reel == null || reel.displayImages == null) return;
        if (row < 0 || row >= reel.displayImages.Count) return;

        Image icon = reel.displayImages[row];
        if (icon != null) icon.gameObject.SetActive(active);
    }

    // Takes the whole win layer down and restores every reel icon underneath it. The restore is
    // deliberately unconditional and paired with the hide in this one method: AnimateWinPositions
    // hides icons per winning cell, and if any of them were missed here that cell would stay blank
    // for the rest of the session. Every teardown path runs through here — between the total and
    // the closing hold, on the next StartSpin, and on Start.
    private void HideWinSlots()
    {
        if (animSlotColumns != null)
        {
            foreach (var column in animSlotColumns)
            {
                if (column == null || column.rows == null) continue;
                foreach (var slot in column.rows)
                {
                    if (slot == null) continue;

                    // Undo TryShowSpineSlot: Spine off, the cell's static Image back for next time.
                    if (slot.spine != null) slot.spine.gameObject.SetActive(false);

                    if (slot.image == null) continue;

                    slot.image.enabled = true;
                    slot.image.gameObject.SetActive(false);
                }
            }
        }

        if (reelImagesList == null) return;
        for (int col = 0; col < reelImagesList.Count; col++)
        {
            var reel = reelImagesList[col];
            if (reel == null || reel.displayImages == null) continue;

            for (int row = 0; row < reel.displayImages.Count; row++)
            {
                if (reel.displayImages[row] != null) reel.displayImages[row].gameObject.SetActive(true);
            }
        }
    }

    /// <summary>
    /// Shows one win's line during the walk: its payline image, and its payout on the MIDDLE REEL
    /// (reel 3), in the row the line passes through there — the row of that reel's box, which is why
    /// three labels cover every win.
    ///
    /// The row is read from the payline itself (gameConfig.paylines[lineId]), so a 2-symbol win that
    /// stops short of reel 3 still puts its amount where its line crosses it. Without the payline
    /// table it falls back to the win's own cell on reel 3, then to the middle row.
    /// </summary>
    private void ShowWinLine(WinLine winLine)
    {
        if (winLine == null) return;

        ShowPaylineImage(winLine.lineId);

        if (winLineAmounts == null) return;

        int middleReel = ReelCount / 2;
        int row = RowCount / 2;

        List<List<int>> paylines = gameManager != null && gameManager.gameConfig != null
            ? gameManager.gameConfig.paylines
            : null;
        List<int> payline = paylines != null && winLine.lineId >= 0 && winLine.lineId < paylines.Count
            ? paylines[winLine.lineId]
            : null;

        if (payline != null && middleReel < payline.Count)
        {
            row = payline[middleReel];
        }
        else if (winLine.positions != null)
        {
            foreach (int flatIndex in winLine.positions)
            {
                if (flatIndex % ReelCount != middleReel) continue;

                row = flatIndex / ReelCount;
                break;
            }
        }

        if (row < 0 || row >= winLineAmounts.Length)
        {
            Debug.LogWarning($"[SlotView] Win line row {row} is outside winLineAmounts ({winLineAmounts.Length} entries) — no amount shown.");
            return;
        }

        TMPro.TMP_Text label = winLineAmounts[row];
        if (label == null)
        {
            Debug.LogWarning($"[SlotView] No win amount label assigned for row {row} — the line will show without its payout.");
            return;
        }

        // Sprite digits, matching the total win. ToSpriteMoney goes through MoneyFormat, so the two
        // can never disagree about how an amount is written.
        label.text = SpriteTextFormatter.ToSpriteMoney(winLine.winAmount);

        // The same pop as the total (owner, 2026-11-10); it switches the label on.
        AnimateAmountPop(label.transform, WinLineAmountSceneScale(row));
    }

    // The winning line's sprite on the one payline image, and the image on. A line with no sprite
    // leaves the image off rather than showing the previous line's.
    private void ShowPaylineImage(int lineId)
    {
        if (paylineImage == null) return;

        Sprite sprite = paylineSprites != null && lineId >= 0 && lineId < paylineSprites.Count
            ? paylineSprites[lineId]
            : null;

        if (sprite == null)
        {
            paylineImage.gameObject.SetActive(false);
            return;
        }

        paylineImage.sprite = sprite;
        paylineImage.gameObject.SetActive(true);
    }

    // All three amounts, not just the one showing — the walk moves between rows, and only the row
    // about to be shown gets switched on again — and the payline image with them.
    private void HideAllWinLines()
    {
        if (paylineImage != null) paylineImage.gameObject.SetActive(false);

        if (winLineAmounts == null) return;

        for (int row = 0; row < winLineAmounts.Length; row++)
        {
            TMPro.TMP_Text label = winLineAmounts[row];
            if (label == null) continue;

            // Back to its scene scale, in case it was hidden mid-pop.
            label.transform.DOKill();
            label.transform.localScale = WinLineAmountSceneScale(row);
            label.gameObject.SetActive(false);
        }
    }

    // The win layer always comes down; the dim itself is skipped while something is holding it up.
    //
    // Both holds below are inert today (nothing sets either flag). Anything that raises the dim before
    // the win presentation and means the presentation to inherit it needs one: without it,
    // ShowWinLineAnimation's opening KillWinTweens drops the dim a frame before it is raised again,
    // which reads as a flicker.
    private void HideWinDim()
    {
        if (winAnimationLayer != null) winAnimationLayer.SetActive(false);

        // A feature round's hold is the same kind of guard: a round owning the dim for its whole
        // duration means a win teardown inside the round cannot take it down.
        if (dimHeld || featureDimHeld) return;
        if (winDimOverlay != null) winDimOverlay.SetActive(false);
    }

    // Releases the per-spin claim on the dim and takes it down. Called at the end of the whole
    // presentation, so a spin that raised the dim and then produced no win still clears correctly.
    private void ReleaseHeldDim()
    {
        dimHeld = false;

        // Releasing the per-spin claim does not release a feature round's.
        if (featureDimHeld) return;
        if (winDimOverlay != null) winDimOverlay.SetActive(false);
    }

    #endregion


    private void KillAllTweens()
    {
        foreach (var tween in spinTweens)
        {
            tween?.Kill();
        }
        spinTweens.Clear();

        KillWinTweens();
    }

    #region Cleanup

    private void OnDestroy()
    {
        KillAllTweens();
    }

    #endregion
}

// One win-animation slot: the Image that shows the symbol, the ImageAnimation that plays its frames,
// and the optional Spine object (a child of the Image, built by Tools > SL-AT > Build Win Spine Slots)
// that plays a symbol's Spine animation instead when it has one.
// All are wired explicitly rather than found with GetComponent — a missing component would
// otherwise just silently no-op, and these icons must have one while the reel icons must not.
// Kept in a single struct so they can never drift out of step with each other.
[System.Serializable]
public class AnimSlot
{
    public Image image;
    public ImageAnimation animation;
    public SpineAnimController spine;
}

// One reel column's worth of win-animation slots. These live on a layer above the dim overlay,
// so a winning symbol can be shown bright while the real reel icon stays dimmed underneath.
// One slot per visible cell — 3 rows, matching the grid exactly.
[System.Serializable]
public class AnimSlotColumn
{
    public List<AnimSlot> rows = new List<AnimSlot>(3);   // index 0 = top active row
}

[System.Serializable]
public class ReelImages
{
    // Pure scroll buffer — everything except the real display-block icons below.
    public List<Image> images = new List<Image>(16);
    // Direct references to the real display-block icons, top row first — one per visible row, so
    // a row index here means the same thing it does in the server matrix. Wired manually per reel
    // in the Inspector, not derived from bufferRowsAbove, so each reel's buffer icon count can
    // differ without breaking which icons show the real backend result.
    public List<Image> displayImages = new List<Image>(3);
}