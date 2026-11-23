using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] internal SocketIOManager socketManager;
    [SerializeField] internal UIManager uiManager;
    [SerializeField] private PopupManager popupManager;
    [SerializeField] private SlotView slotView;
    [Tooltip("The free games' screen (FreeGameView) — the transition, the background's sprite swap, the top panel and TOTAL PRIZE.")]
    [SerializeField] private FreeGameView freeGameView;
    [Tooltip("The free games' popups (FeaturePopupView) — \"N FREE GAMES\" and CONGRATULATIONS / COLLECT.")]
    [SerializeField] private FeaturePopupView featurePopupView;
    [Tooltip("The free games' layer over the board (FramesView) — the frames, their effect, the end orbs and their prizes.")]
    [SerializeField] private FramesView framesView;

    [Header("Spin Settings")]
    [SerializeField] private float normalSpinDuration = 3.5f;
    [SerializeField] private float turboSpinDuration = 2.0f;
    [SerializeField] private float quickSpinCycleDuration = 0.1f;

    [Header("Free Games Timings (ATFreeGamesBonus.md)")]
    [Tooltip("How long FEATURE COMPLETE holds before the prize reveal (§5.6; owner: about 1 s).")]
    [SerializeField] private float featureCompleteHold = 1f;
    [Tooltip("A free game whose frames changed but that won nothing: the hold AFTER the frame effect ends, before the next game (§5.4; owner: 0.5 s).")]
    [SerializeField] private float frameOnlyHold = 0.5f;
    [Tooltip("A free game with no frame change and no win: the hold before the next game (§5.4; owner: 0.5 s).")]
    [SerializeField] private float nothingHold = 0.5f;
    [Tooltip("How long the win box takes to count a free game's win up onto the running total (§5.4).")]
    [SerializeField] private float winCountUpDuration = 1f;

    // The big-win popup: on again (owner, 2026-11-20) with AT's big_win Spine — one tier, at
    // bigWinMultiplierThreshold, base game only. The feature's special cases come later.
    private const bool BigWinPopupEnabled = true;

    [Header("Win Settings")]
    // Win / total bet at or above which the big-win popup plays. Serialized, so the scene's value is
    // the one that runs — this default only matters for a fresh component. Kept equal to the scene.
    // TODO (owner, 2026-11-23): the SCENE value is lowered to 0.1 for now so big wins come up while
    // testing — FIX LATER: set the scene back to the real threshold, 10x (this default already is).
    [SerializeField] private double bigWinMultiplierThreshold = 10;

    internal GameConfig gameConfig;
    internal PlayerData playerData;
    internal SpinResult lastResult;

    internal GameState currentState;
    internal SpinSpeed currentSpinSpeed;

    internal int currentBetIndex;
    internal double currentBetAmount;

    internal bool isAutoPlaying;
    internal int autoPlayTotalRounds;
    internal int autoPlayRemainingRounds;
    internal bool wasAutoPlayingBeforeFeature;
    internal int savedAutoPlayRemainingRounds;
    internal int savedAutoPlayTotalRounds;

    // The free games (ATFreeGamesBonus.md) — true from the transition in to the transition out. The
    // name is inherited from the free-spins games; UIManager and SlotView read it.
    internal bool isInFreeSpins;
    // What the win box shows during the bonus: the running total as the player has seen it — each
    // game's win counted up onto it, each end orb's prize added. Ends on the server's totalWin.
    internal double freeSpinsRoundWin;
    // The round's games so far (the server's totalAwarded) — a retrigger adds the change in it.
    private int freeGamesTotalAwarded;
    // The count the top panel shows during the next game: the last result's remaining.
    private int freeGamesShownRemaining;
    // The board the bonus was triggered on, put back on the reels when it ends (§8).
    private List<List<int>> triggerMatrix;

    // A feature holding the shown balance back while the server's already includes its pay: the free
    // games, for the whole round — the server credits each game as it is won, the player sees it all
    // at the end (ATFreeGamesBonus.md §8, §11.5 item 5).
    private bool balanceHeld;
    private double heldBalance;

    // The balance the UI shows. playerData always holds the true, server balance; this is the one
    // place a feature holds the display back.
    internal double DisplayBalance => balanceHeld ? heldBalance : playerData.balance;

    // What the shared button does in its FeatureStart / FeatureTake modes, set by the feature that
    // put it there and cleared on the press.
    private System.Action pendingFeatureStart;
    private System.Action pendingFeatureTake;

    internal bool isInitialized;
    internal bool initializationFailed;

    private Coroutine spinCoroutine;
    private bool stopRequested;
    private bool waitingForSpecialWin;

    #region Initialization

    private void Start()
    {
        currentState = GameState.Initializing;
        currentSpinSpeed = SpinSpeed.Normal;
        isInitialized = false;
        initializationFailed = false;
    }

    internal void OnInitDataReceived(GameConfig config, PlayerData player, List<List<int>> initialMatrix)
    {
        gameConfig = config;
        playerData = player;
        currentBetIndex = playerData.currentBetIndex;
        UpdateBetAmount();

        if (initialMatrix != null && slotView != null)
        {
            slotView.SetInitialMatrix(initialMatrix);
        }

        isInitialized = true;
        currentState = GameState.Idle;

        // The feature views start in the base game's look whatever the scene was saved with: base
        // background, no top panel, no popups, no frames or orbs.
        if (freeGameView != null) freeGameView.ResetToBaseGame();
        if (featurePopupView != null) featurePopupView.HideImmediate();
        if (framesView != null)
        {
            framesView.Configure(gameConfig.reelCount);
            // The end orbs play the jewel Spines, which SlotView owns (owner, 2026-11-19).
            if (slotView != null) framesView.SetOrbSpines(slotView.BlueOrbSpine, slotView.PurpleOrbSpine, slotView.YellowOrbSpine);
            framesView.ResetAll();
        }
        if (slotView != null) slotView.SetFreeGamesFiller(false);

        uiManager.OnGameInitialized();
    }

    #endregion

    #region Bet Management

    internal void IncreaseBet()
    {
        if (!CanChangeBet()) return;

        int maxIndex = gameConfig.availableBets.Count - 1;
        int nextIndex = currentBetIndex + 1;
        if (nextIndex > maxIndex)
        {
            nextIndex = 0;
        }

        if (nextIndex == maxIndex)
        {
            AudioManager.Instance?.PlayMaxBetReached();
        }
        else
        {
            AudioManager.Instance?.PlayBetPlusMinus();
        }

        SetBetIndex(nextIndex);
    }

    internal void DecreaseBet()
    {
        if (!CanChangeBet()) return;

        int maxIndex = gameConfig.availableBets.Count - 1;
        int nextIndex = currentBetIndex - 1;
        if (nextIndex < 0)
        {
            nextIndex = maxIndex;
        }

        if (nextIndex == maxIndex)
        {
            AudioManager.Instance?.PlayMaxBetReached();
        }
        else
        {
            AudioManager.Instance?.PlayBetPlusMinus();
        }

        SetBetIndex(nextIndex);
    }

    // The bet is fixed for a whole feature. Between free spins the state is briefly Idle, so Idle alone
    // doesn't keep it out — and a bet changed mid-round would shift the big-win threshold for spins
    // the player already started at the old bet. The locked buttons stop it in practice; this is the
    // guard behind them.
    private bool CanChangeBet()
    {
        if (currentState != GameState.Idle || isAutoPlaying) return false;
        if (isInFreeSpins) return false;
        return gameConfig != null && gameConfig.availableBets != null && gameConfig.availableBets.Count > 0;
    }

    internal void SetBetIndex(int index)
    {
        currentBetIndex = index;
        UpdateBetAmount();
        uiManager.UpdateBetDisplay();
        if (slotView != null) slotView.OnBetChanged();
    }

    private void UpdateBetAmount()
    {
        currentBetAmount = gameConfig.availableBets[currentBetIndex];
    }

    #endregion

    #region Spin Control
    
    internal void RequestSpin()
    {
        if (currentState != GameState.Idle) return;
        if (!socketManager.isConnected) return;

        double totalPay = GetTotalPay();
        if (!isInFreeSpins && playerData.balance < totalPay)
        {
            if (popupManager != null)
            {
                popupManager.ShowInsufficientFundsError();
            }
            return;
        }

        StartSpin();
    }

    internal void RequestStop()
    {
        if (currentState == GameState.Spinning)
        {
            if (isAutoPlaying)
            {
                StopAutoPlay();
            }
            // Free spins included: STOP quick-stops the current free spin's reels, exactly as in the
            // base game (owner). It does not stop the round — the next free spin still follows.
            else
            {
                stopRequested = true;
                uiManager.SetSpinStopButtonStates(isSpinningState: true, isInteractable: false);
            }
        }
    }

    private void StartSpin()
    {
        if (lastResult != null)
        {
            ProcessSpinResult();
        }

        lastResult = null;
        currentState = GameState.Spinning;
        stopRequested = false;

        // Deduct total pay from balance on spin start (except in free spins, which the server plays
        // for free — the triggering spin was the paid one).
        if (!isInFreeSpins)
        {
            playerData.balance -= GetTotalPay();
            if (playerData.balance < 0) playerData.balance = 0;
        }

        uiManager.OnSpinStarted();

        // A free game: the count goes down as it starts (it includes this game — "LAST SPIN" at 1),
        // and the reels all start together (ATFreeGamesBonus.md §5.1, §5.2).
        if (isInFreeSpins && freeGameView != null) freeGameView.ShowRemaining(freeGamesShownRemaining);

        if (slotView != null)
        {
            slotView.StartSpin(together: isInFreeSpins);
        }

        socketManager.SendSpinRequest(currentBetIndex);

        if (spinCoroutine != null)
            StopCoroutine(spinCoroutine);
        spinCoroutine = StartCoroutine(SpinRoutine());
    }

    private IEnumerator SpinRoutine()
    {
        float spinDuration = GetSpinDuration();
        float elapsed = 0f;

        while (elapsed < spinDuration && !stopRequested)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Player pressed Stop manually — hold for 0.5s so the reels keep
        // spinning briefly before snapping, giving clear visual feedback.
        if (stopRequested)
        {
            yield return new WaitForSeconds(0.5f);
        }

        while (lastResult == null)
        {
            yield return null;
        }

        currentState = GameState.Stopping;

        List<List<int>> landingMatrix = lastResult.resultMatrix;
        bool quickStop = currentSpinSpeed == SpinSpeed.QuickSpin || stopRequested;

        if (slotView != null && landingMatrix != null)
        {
            if (quickStop)
            {
                // The view reports when the snap has settled, as the normal stop does — never a fixed
                // wait, which would break as soon as the quick-stop timings are retuned.
                slotView.QuickStop(landingMatrix, OnReelsStoppedComplete);
            }
            else
            {
                slotView.StopSpin(landingMatrix, OnReelsStoppedComplete);
            }
        }
        else
        {
            OnReelsStoppedComplete();
        }
    }

    private void OnReelsStoppedComplete()
    {
        // Safety net. StopSpinSequence already cuts the loop at the exact landing moment, but it is
        // bypassed entirely when SlotView has no reels or no result matrix to stop onto. Without this
        // the loop would run until the next spin restarted it. No-ops when already stopped.
        AudioManager.Instance?.StopSpinLoop();

        if (lastResult != null)
        {
            playerData = new PlayerData
            {
                balance = lastResult.playerData != null ? lastResult.playerData.balance : 0,
                currentBetIndex = lastResult.playerData != null ? lastResult.playerData.currentBetIndex : currentBetIndex
            };
        }

        // Anything that has to alter the board between the reels landing and the win being presented
        // belongs BEFORE PresentSpinOutcome, running it from its own completion callback rather than
        // alongside it. The board has to be final before any win animation starts.
        PresentSpinOutcome();
    }

    // Everything that happens once the board is final.
    private void PresentSpinOutcome()
    {
        // A free game plays its own outcome: frames, wins, a retrigger, then the next game or the end.
        if (isInFreeSpins)
        {
            StartCoroutine(PlayFreeGame(lastResult));
            return;
        }

        // A base-game spin that triggers the free games hands the game over to the bonus.
        if (IsFreeGamesTrigger(lastResult))
        {
            StartCoroutine(PlayFreeGamesTrigger(lastResult));
            return;
        }

        if (lastResult != null && lastResult.winAmount > 0 && lastResult.winLines != null && lastResult.winLines.Count > 0)
        {
            double totalPay = GetTotalPay();
            double multiplier = totalPay > 0 ? (lastResult.winAmount / totalPay) : 0;

            // The big-win popup (BigWinPopupEnabled) never shows during free spins (owner): they show
            // their wins and move on.
            if (IsBigWin(multiplier))
            {
                uiManager.DisableControlsDuringWinAnimation();
                currentState = GameState.Idle;
                slotView.ShowWinLineAnimation(lastResult.winLines, lastResult.winAmount, OnWinAnimationComplete);
                StartCoroutine(TriggerWinPopupWithDelay(1.5f, lastResult));
            }
            else
            {
                // For normal wins, trigger UI update immediately and enable controls
                uiManager.OnSpinStopping(lastResult);
                uiManager.EnableControlsAfterWinAnimation();
                uiManager.OnSpinCompleted(lastResult);
                currentState = GameState.Idle;
                slotView.ShowWinLineAnimation(lastResult.winLines, lastResult.winAmount, OnWinAnimationComplete);
            }
        }
        else
        {
            uiManager.OnSpinStopping(lastResult);
            currentState = GameState.Idle;

            // Still handed to the view, with nothing to present: SlotView owns the dim, and a losing
            // spin may still need it taken down.
            if (slotView != null)
            {
                slotView.ShowWinLineAnimation(null, 0, OnWinAnimationComplete);
            }
            else
            {
                OnWinAnimationComplete();
            }
        }
    }

    // Whether a win of this many times the total bet gets the big-win popup: never while it is switched
    // off, and never during free spins.
    private bool IsBigWin(double multiplier)
    {
        return BigWinPopupEnabled && !isInFreeSpins && multiplier >= bigWinMultiplierThreshold;
    }

    private IEnumerator TriggerWinPopupWithDelay(float delay, SpinResult result)
    {
        double totalPay = GetTotalPay();
        double multiplier = totalPay > 0 ? (result.winAmount / totalPay) : 0;
        if (multiplier < bigWinMultiplierThreshold)
        {
            waitingForSpecialWin = false;
            yield break;
        }

        waitingForSpecialWin = true;

        yield return new WaitForSeconds(delay);

        if (lastResult == result && multiplier >= bigWinMultiplierThreshold)
        {
            uiManager.TriggerBigWinPopup(result, () =>
            {
                waitingForSpecialWin = false;
            });
        }
        else
        {
            waitingForSpecialWin = false;
        }
    }

    private void OnWinAnimationComplete()
    {
        if (lastResult != null)
        {
            double totalPay = GetTotalPay();
            double multiplier = totalPay > 0 ? (lastResult.winAmount / totalPay) : 0;

            // Only update UI here if it wasn't already updated in PresentSpinOutcome — a big win.
            if (IsBigWin(multiplier))
            {
                uiManager.OnSpinStopping(lastResult);
            }
        }

        StartCoroutine(ProcessSpecialFeaturesAfterWin());
    }

    private IEnumerator ProcessSpecialFeaturesAfterWin()
    {
        // Wait for special win popup to finish before starting special features
        while (waitingForSpecialWin || uiManager.IsSpecialWinActive)
        {
            yield return null;
        }

        ResumeAfterSpecialFeature();
    }

    // The base game's way on after a spin. The free games never come through here — PlayFreeGame
    // carries the bonus on itself.
    private void ResumeAfterSpecialFeature()
    {
        if (isAutoPlaying)
        {
            StartCoroutine(DelayBeforeNextRound());
        }
        else
        {
            ProcessSpinResult();
        }
    }

    private IEnumerator DelayBeforeNextRound()
    {
        float delayTime = currentSpinSpeed == SpinSpeed.QuickSpin ? 0.3f : 0.5f;
        yield return new WaitForSeconds(delayTime);

        // Wait for special win popup using the flag and active state
        while (waitingForSpecialWin || uiManager.IsSpecialWinActive)
        {
            yield return null;
        }

        ProcessSpinResult();
    }

    // If a feature round ever wants a spin duration of its own, shorten it by the same proportion
    // Turbo shortens a base spin — the base game's Turbo duration can be longer than the feature's
    // own Normal one.
    private float GetSpinDuration()
    {
        return currentSpinSpeed switch
        {
            SpinSpeed.Normal => normalSpinDuration,
            SpinSpeed.Turbo => turboSpinDuration,
            SpinSpeed.QuickSpin => quickSpinCycleDuration,
            _ => normalSpinDuration
        };
    }

    internal void OnSpinResultReceived(SpinResult result)
    {
        lastResult = result;

        // The result is not handed to SlotView here. It writes the landed sprites itself as each reel
        // lands (StopSingleReel), in the same frame as the landing snap, so the swap is never seen.
        // The free games read their figures once the reels have landed (PlayFreeGame).
    }

    private void ProcessSpinResult()
    {
        playerData = lastResult.playerData;

        uiManager.OnSpinCompleted(lastResult);

        // This is where a round is advanced. A feature added here should end its round on the server's own "still active" flag, never on
        // a spins-remaining counter reaching zero — a round can close on the very spin that reset its
        // counter, so the two do not agree.
        lastResult = null;

        if (isAutoPlaying)
        {
            if (autoPlayTotalRounds != -1)
            {
                autoPlayRemainingRounds--;
            }

            uiManager.UpdateAutoPlayCount();

            if (autoPlayTotalRounds != -1 && autoPlayRemainingRounds <= 0)
            {
                currentState = GameState.Idle;
                StopAutoPlay();
            }
            else
            {
                // Before requesting the next spin, verify the player can still afford it.
                // If not, stop autoplay (restores all UI) then show the popup.
                double totalPay = GetTotalPay();
                if (playerData.balance < totalPay)
                {
                    currentState = GameState.Idle;
                    StopAutoPlay();
                    if (popupManager != null) popupManager.ShowInsufficientFundsError();
                }
                else
                {
                    currentState = GameState.Idle;
                    RequestSpin();
                }
            }
        }
        else
        {
            currentState = GameState.Idle;
        }
    }

    #endregion

    #region Spin Speed Control

    internal void SetSpinSpeed(SpinSpeed speed)
    {
        currentSpinSpeed = speed;
    }

    #endregion



    #region Auto Play

    internal void StartAutoPlay(int rounds)
    {
        if (currentState != GameState.Idle) return;

        // Check balance BEFORE locking any UI — if insufficient, show popup and bail.
        double totalPay = GetTotalPay();
        if (playerData.balance < totalPay)
        {
            if (popupManager != null) popupManager.ShowInsufficientFundsError();
            return;
        }

        isAutoPlaying = true;
        autoPlayTotalRounds = rounds;
        autoPlayRemainingRounds = rounds;
        wasAutoPlayingBeforeFeature = false;

        uiManager.OnAutoPlayStarted();
        RequestSpin();
    }

    internal void StopAutoPlay()
    {
        isAutoPlaying = false;
        autoPlayRemainingRounds = 0;
        wasAutoPlayingBeforeFeature = false;

        uiManager.OnAutoPlayStopped();

        // Autoplay skips the per-line cycle while it runs, so the round it just finished is parked
        // after Phase 1. Now that no further spin is coming, present it the way a manual spin would.
        // Covers both endings: the last scheduled round, and the player stopping part-way.
        //
        // Feature rounds are excluded. A round that parks autoplay before it begins may well have
        // paid a line on its triggering spin, and cycling those lines here would run them underneath
        // the feature intro for its whole duration. A feature trigger that parks autoplay in
        // ShowingWin is excluded for the same reason: the cycle would tear its trigger celebration
        // down.
        if (!isInFreeSpins && currentState != GameState.ShowingWin && slotView != null)
        {
            slotView.PlayWinLineCycle();
        }
    }

    internal bool ShouldResumeAutoPlay()
    {
        return wasAutoPlayingBeforeFeature && (savedAutoPlayTotalRounds == -1 || savedAutoPlayRemainingRounds > 0);
    }

    internal void ResumeAutoPlay()
    {
        if (!ShouldResumeAutoPlay()) return;

        int remaining = savedAutoPlayRemainingRounds;
        int total = savedAutoPlayTotalRounds;
        wasAutoPlayingBeforeFeature = false;

        if (currentState != GameState.Idle) return;

        double totalPay = GetTotalPay();
        if (playerData.balance < totalPay)
        {
            if (popupManager != null) popupManager.ShowInsufficientFundsError();
            return;
        }

        isAutoPlaying = true;
        autoPlayTotalRounds = total;
        autoPlayRemainingRounds = remaining;

        uiManager.OnAutoPlayStarted();
        RequestSpin();
    }

    #endregion

    #region Feature Buttons

    // Routed here by UIManager's FeatureStart mode. The feature that showed START set the callback.
    internal void OnFeatureStartPressed()
    {
        var callback = pendingFeatureStart;
        pendingFeatureStart = null;
        callback?.Invoke();
    }

    // Routed here by UIManager's FeatureTake mode. The feature that showed TAKE set the callback.
    internal void OnFeatureTakePressed()
    {
        var callback = pendingFeatureTake;
        pendingFeatureTake = null;
        callback?.Invoke();
    }

    // A feature parks autoplay for its whole length: Start and Take are the player's to press. The
    // trigger spin never reaches ProcessSpinResult's per-round decrement, so it is counted here.
    // StopAutoPlay clears the resume flag, so it is set after.
    private void SuspendAutoPlayForFeature()
    {
        if (!isAutoPlaying) return;

        int prevTotal = autoPlayTotalRounds;
        int prevRemaining = autoPlayRemainingRounds;

        StopAutoPlay();

        wasAutoPlayingBeforeFeature = true;
        savedAutoPlayTotalRounds = prevTotal;
        savedAutoPlayRemainingRounds = (prevTotal != -1) ? (prevRemaining - 1) : -1;
    }

    #endregion

    #region Free Games

    // Aladdin's Treasure's Free Games Bonus (ATFreeGamesBonus.md). GameManager runs the sequence; the
    // views animate and call back. Every figure is the server's (payload.freeGames); the client counts
    // nothing itself but the win box's running total, which it checks against the server's at the end.

    // A base-game spin that triggers the free games.
    private bool IsFreeGamesTrigger(SpinResult result)
    {
        if (isInFreeSpins || result == null || result.freeGames == null) return false;
        return result.freeGames.present && result.freeGames.triggered;
    }

    // The trigger (§3, §4):
    //  1. the game is taken over; the trigger spin's own win only reaches the balance — no win
    //     presentation, the win box untouched (§3.2) — and from here the shown balance is held;
    //  2. the BlueOrbs loop (no dim);
    //  3. "N FREE GAMES" pops in, holds, pops out;
    //  4. the transition — behind it the orbs stop, the free-games background and top panel come in,
    //     the win box goes to 0 and the filler switches to the free games';
    //  5. after it, in view, the Blue frames appear on the triggering positions with their effect;
    //  6. START.
    private IEnumerator PlayFreeGamesTrigger(SpinResult result)
    {
        FreeGamesData award = result.freeGames;
        triggerMatrix = result.resultMatrix;

        BeginFeatureSession(result);

        heldBalance = playerData != null ? playerData.balance : 0;
        balanceHeld = true;
        uiManager.UpdateBalanceDisplay();

        int orbId = gameConfig != null ? gameConfig.freeSpinsSymbolId : -1;
        if (slotView != null) slotView.LoopSymbolCells(orbId);

        yield return ShowGamesAwarded(award.totalAwarded);

        void Covered()
        {
            if (slotView != null)
            {
                slotView.ClearWinPresentation();
                slotView.SetFreeGamesFiller(true);
            }

            isInFreeSpins = true;
            freeGamesTotalAwarded = award.totalAwarded;
            freeGamesShownRemaining = award.remaining;
            freeSpinsRoundWin = 0;

            if (freeGameView != null)
            {
                freeGameView.ShowFreeGamesLook(true);
                freeGameView.ShowRemaining(award.remaining);
            }

            uiManager.SetWinDisplay(0);
            AudioManager.Instance?.PlayFreeSpinsMusic();
        }

        yield return PlayTransition(Covered);

        bool framesShown = framesView == null;
        if (framesView != null) framesView.ShowFrames(award.frameLevels, () => framesShown = true);
        yield return new WaitUntil(() => framesShown);

        pendingFeatureStart = BeginFreeGames;
        uiManager.SetSpinButtonMode(UIManager.SpinButtonMode.FeatureStart);
    }

    // START: the first free game, straight away. The rest follow on their own.
    private void BeginFreeGames()
    {
        currentState = GameState.Idle;

        // Out of the explicit START mode, so the games can put the button into Stop.
        uiManager.ReleaseSpinButton(false);
        RequestSpin();
    }

    // One free game, once its reels have landed (§5.3–§5.5):
    //  1. its frame changes, all together, each with the effect (an Orange hit has no visual yet — V-9);
    //  2. its ways wins — the overall win only — with the win box counting this game's win (ways +
    //     Orange hit) up onto the running total; or a short hold when it won nothing;
    //  3. a retrigger, when the round's total grew: the orbs loop and "N FREE GAMES" shows the games
    //     actually added (a retrigger at the cap adds none and plays as an ordinary game);
    //  4. the next game — or, after the last, the end phase.
    private IEnumerator PlayFreeGame(SpinResult result)
    {
        currentState = GameState.ShowingWin;
        uiManager.SetSpinStopButtonStates(isSpinningState: true, isInteractable: false);

        FreeGamesData game = result != null ? result.freeGames : null;
        if (game == null || !game.present)
        {
            // Should never happen inside the bonus. Ending the round is the one safe reading: the
            // win box and CONGRATULATIONS show what has been shown so far, the balance the server's.
            Debug.LogError("[GameManager] A free game's result carried no freeGames block — ending the round.");
            lastResult = null;
            yield return PlayFreeGamesEnd(null);
            yield break;
        }

        // 1. Frames.
        bool framesChanged = false;
        foreach (FrameChange change in game.changes)
        {
            if (!change.IsOrangeHit) { framesChanged = true; break; }
        }

        if (game.changes.Count > 0 && framesView != null)
        {
            bool applied = false;
            framesView.ApplyChanges(game.changes, () => applied = true);
            yield return new WaitUntil(() => applied);
        }

        // 2. Wins. The Orange hit joins this game's count-up for now (owner, 2026-11-15) — the place
        // to change once its presentation is known (V-9).
        double gameWin = result.lineWinAmount + game.orangeHitCash;
        if (gameWin > 0)
        {
            uiManager.CountWinDisplay(freeSpinsRoundWin, freeSpinsRoundWin + gameWin, winCountUpDuration);
            freeSpinsRoundWin += gameWin;
        }

        bool hasWays = result.lineWinAmount > 0 && result.winLines != null && result.winLines.Count > 0;
        if (hasWays && slotView != null)
        {
            // Free games stop after the total — every winning symbol twice, in step — and the next
            // game follows (SlotView.PlayTwoPhaseWinLines skips the walk while isInFreeSpins).
            bool presented = false;
            slotView.ShowWinLineAnimation(result.winLines, result.lineWinAmount, () => presented = true);
            yield return new WaitUntil(() => presented);
        }
        else
        {
            // Frames but no win: 0.5 s after the effect has ended. Nothing at all: 0.5 s.
            yield return new WaitForSeconds(framesChanged ? frameOnlyHold : nothingHold);
        }

        // 3. Retrigger.
        int added = game.totalAwarded - freeGamesTotalAwarded;
        if (game.totalAwarded > 0) freeGamesTotalAwarded = game.totalAwarded;

        if (added > 0)
        {
            if (slotView != null) slotView.LoopSymbolCells(gameConfig != null ? gameConfig.freeSpinsSymbolId : -1);
            yield return ShowGamesAwarded(added);
            if (slotView != null) slotView.ClearWinPresentation();

            // The count goes up by what was added; it comes down by one as the next game starts.
            if (freeGameView != null) freeGameView.ShowRemaining(freeGamesShownRemaining + added);
        }

        freeGamesShownRemaining = game.remaining;

        // 4. On.
        lastResult = null;
        uiManager.UpdateBalanceDisplay();

        if (game.isCompleted || !game.inFreeGames)
        {
            yield return PlayFreeGamesEnd(game);
            yield break;
        }

        currentState = GameState.Idle;
        RequestSpin();
    }

    // The end (§5.6–§8):
    //  1. FEATURE COMPLETE, briefly;
    //  2. every framed position empties — frame and symbol;
    //  3. TOTAL PRIZE (blank) and every end orb pop in together;
    //  4. one orb at a time — all Blue, then Purple, then Yellow; within a colour reel by reel, top to
    //     bottom — plays its jewel Spine once, fades away, and its prize appears; TOTAL PRIZE counts up
    //     by it and the win box steps up as the number appears (owner, 2026-11-19);
    //  5. CONGRATULATIONS counts up to the bonus total; COLLECT;
    //  6. the transition — behind it the base game comes back with the triggering board, the balance
    //     is released and the win box keeps the total until the next spin.
    // last is null only when the bonus has to end without its last result.
    private IEnumerator PlayFreeGamesEnd(FreeGamesData last)
    {
        currentState = GameState.ShowingWin;
        uiManager.SetSpinStopButtonStates(isSpinningState: false, isInteractable: false);

        if (freeGameView != null) freeGameView.ShowFeatureComplete();
        yield return new WaitForSeconds(featureCompleteHold);

        var prizes = new List<EndPrize>();
        if (last != null && last.endPrizes != null) prizes.AddRange(last.endPrizes);

        int reelCount = gameConfig != null ? gameConfig.reelCount : 5;
        prizes.Sort((a, b) =>
        {
            if (a.level != b.level) return a.level.CompareTo(b.level);                          // Blue, Purple, Yellow
            int byReel = (a.cell % reelCount).CompareTo(b.cell % reelCount);                  // reel by reel
            return byReel != 0 ? byReel : (a.cell / reelCount).CompareTo(b.cell / reelCount); // top to bottom
        });

        var cells = new List<int>(prizes.Count);
        foreach (EndPrize prize in prizes) cells.Add(prize.cell);

        // Any win presentation goes first: its teardown would put the hidden symbols back.
        if (slotView != null)
        {
            slotView.ClearWinPresentation();
            slotView.SetCellsHidden(cells);
        }
        if (framesView != null) framesView.ResetAll();

        if (prizes.Count > 0)
        {
            if (freeGameView != null) freeGameView.PopInTotalPrize();

            bool popped = framesView == null;
            if (framesView != null) framesView.PopInOrbs(prizes, () => popped = true);
            yield return new WaitUntil(() => popped);

            double prizeSoFar = 0;
            foreach (EndPrize prize in prizes)
            {
                prizeSoFar += prize.cash;
                freeSpinsRoundWin += prize.cash;

                // The totals move when the orb's number appears — after its animation and fade.
                double totalPrize = prizeSoFar;
                double roundWin = freeSpinsRoundWin;
                float countDuration = framesView != null ? framesView.PrizeHold : 0f;
                void PrizeShown()
                {
                    if (freeGameView != null) freeGameView.CountTotalPrizeTo(totalPrize, countDuration);
                    uiManager.SetWinDisplay(roundWin);
                }

                if (framesView == null)
                {
                    PrizeShown();
                    continue;
                }

                bool revealed = false;
                framesView.RevealOrb(prize, PrizeShown, () => revealed = true);
                yield return new WaitUntil(() => revealed);
            }
        }

        // The server's total is the one that counts.
        if (last != null)
        {
            if (System.Math.Abs(freeSpinsRoundWin - last.totalWin) > 0.005)
            {
                Debug.LogWarning($"[GameManager] Free games: the win box reached {freeSpinsRoundWin:0.00} but the server's total is {last.totalWin:0.00} — showing the server's.");
            }
            freeSpinsRoundWin = last.totalWin;
        }
        uiManager.SetWinDisplay(freeSpinsRoundWin);

        bool collected = featurePopupView == null;
        if (featurePopupView != null) featurePopupView.ShowCongratulations(freeSpinsRoundWin, () => collected = true);
        yield return new WaitUntil(() => collected);

        void Covered()
        {
            if (featurePopupView != null) featurePopupView.HideImmediate();
            if (framesView != null) framesView.ResetAll();
            if (freeGameView != null) freeGameView.ShowFreeGamesLook(false);

            if (slotView != null)
            {
                slotView.SetFreeGamesFiller(false);
                if (triggerMatrix != null) slotView.RestoreBoard(triggerMatrix);
            }

            // The hold is off: the balance shown is the server's again.
            balanceHeld = false;
            uiManager.UpdateBalanceDisplay();
            AudioManager.Instance?.PlayBgMusic();
        }

        yield return PlayTransition(Covered);

        double bonusTotal = freeSpinsRoundWin;
        isInFreeSpins = false;
        freeGamesTotalAwarded = 0;
        freeGamesShownRemaining = 0;
        freeSpinsRoundWin = 0;
        triggerMatrix = null;
        lastResult = null;

        // Spin again, without clearing the win box: it keeps the bonus total until the next spin.
        uiManager.ReleaseSpinButton(true);
        uiManager.SetFeatureButtonLock(false);
        uiManager.SetWinDisplay(bonusTotal);
        currentState = GameState.Idle;

        if (ShouldResumeAutoPlay())
        {
            ResumeAutoPlay();
        }
    }

    // "N FREE GAMES"; returns once the popup has gone.
    private IEnumerator ShowGamesAwarded(int games)
    {
        bool closed = featurePopupView == null;
        if (featurePopupView != null) featurePopupView.ShowGamesAwarded(games, () => closed = true);
        yield return new WaitUntil(() => closed);
    }

    // The full-screen transition; covered runs while it hides the screen. Returns once it has ended.
    private IEnumerator PlayTransition(System.Action covered)
    {
        if (freeGameView == null)
        {
            covered?.Invoke();
            yield break;
        }

        bool done = false;
        freeGameView.PlayTransition(covered, () => done = true);
        yield return new WaitUntil(() => done);
    }

    #endregion

    #region Feature Session

    // The game is the feature's from here: ShowingWin before autoplay is parked, so StopAutoPlay doesn't
    // start a line walk under it; the buttons locked, Spin greyed. Autoplay or not, a feature waits for
    // the player (owner); it resumes at the hand-back.
    //
    // The spin is closed out here: left in lastResult, a later StartSpin would hand it to
    // ProcessSpinResult — which, once a round has been entered, would schedule a second spin.
    private void BeginFeatureSession(SpinResult result)
    {
        currentState = GameState.ShowingWin;
        SuspendAutoPlayForFeature();
        uiManager.SetFeatureButtonLock(true);
        uiManager.SetSpinStopButtonStates(isSpinningState: false, isInteractable: false);

        if (result.playerData != null) playerData = result.playerData;
        lastResult = null;
    }

    #endregion

    #region Connection Events

    internal void OnDisconnected()
    {
        if (spinCoroutine != null)
        {
            StopCoroutine(spinCoroutine);
            spinCoroutine = null;
        }

        wasAutoPlayingBeforeFeature = false;
        if (isAutoPlaying)
        {
            StopAutoPlay();
        }

        currentState = GameState.Idle;
        // Note: The disconnection popup is shown by SocketIOManager.OnSocketDisconnected()
        // to avoid duplicates. GameManager only cleans up state here.
    }

    internal void ExitGame()
    {
        socketManager.CloseSocket();

    }

    #endregion

    #region Helper Methods

    internal double GetTotalPay()
    {
        double activeLine = (gameConfig != null && gameConfig.activeLine > 0) ? gameConfig.activeLine : 20;
        return currentBetAmount * activeLine;
    }

    internal bool IsSpinning()
    {
        return currentState == GameState.Spinning || currentState == GameState.Stopping;
    }

    #endregion
}