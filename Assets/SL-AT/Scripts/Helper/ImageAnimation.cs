using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ImageAnimation : MonoBehaviour
{
    public enum ImageState
    {
        NONE,
        PLAYING,
        PAUSED
    }

    // SINGLE_PHASE is the original behaviour and the default, so every animation already in the scene
    // keeps behaving exactly as before. TWO_PHASE splits one frame list into an opening that plays and
    // a loop that follows it — for things like the big-win popup, which open once and then idle.
    public enum AnimationMode
    {
        SINGLE_PHASE,
        TWO_PHASE
    }

    public static ImageAnimation Instance;

    public List<Sprite> textureArray;
    public Image rendererDelegate;
    public bool useSharedMaterial = true;
    [Tooltip("SINGLE_PHASE only. TWO_PHASE ignores this — its two loop counts decide when it ends.")]
    public bool doLoopAnimation = true;

    public System.Action<int> onLoopComplete;
    private int currentLoopCount = 0;

    [SerializeField] private bool StartOnAwake;
    [SerializeField] private bool StartonEnable;

    [HideInInspector]
    public ImageState currentAnimationState;

    private int indexOfTexture;
    private float idealFrameRate = 0.0416666679f; // ~24 fps
    private float delayBetweenAnimation;

    public float AnimationSpeed = 5f;
    public float delayBetweenLoop;

    // ── Two-phase animation ────────────────────────────────────────────────────────────────────
    // The field names match the two-phase ImageAnimation used in our other games, so the Inspector
    // reads the same across projects. The implementation is NOT that version's: it is merged into this
    // one so the two fixes below (the resurrection check in AnimationProcess and the unconditional
    // cancel in StopAnimation) cover two-phase playback too. The other version's "dynamic framerate"
    // mode was deliberately left out — combined with two phases it never hands over from the opening
    // to the loop, because its frame index is capped one short of the value that triggers the switch.
    //
    // Frames are assigned as ONE list, opening first: 0 .. phase2StartIndex-1 is the opening, and
    // phase2StartIndex .. end is the loop.
    [Header("Two Phase Animation (Optional)")]
    [Tooltip("SINGLE_PHASE plays the whole list as one clip (the original behaviour). TWO_PHASE plays an opening, then loops the rest.")]
    public AnimationMode animationMode = AnimationMode.SINGLE_PHASE;

    [Tooltip("First frame of the LOOP. Frames before it are the opening. 0 = no opening (loop only); the list's count = no loop (opening only).")]
    public int phase2StartIndex = 0;

    [Tooltip("How many times the opening plays. 1 = once (the usual case), -1 = forever, 0 = skip it.")]
    public int phase1LoopCount = 1;

    [Tooltip("How many times the loop plays. -1 = forever (the usual case), 0 = skip it, 1+ = that many then stop on the last frame.")]
    public int phase2LoopCount = -1;

    [Tooltip("Off: both phases use AnimationSpeed. On: the loop uses phase2AnimationSpeed instead.")]
    public bool useCustomSpeed = false;

    [Tooltip("The loop's speed when useCustomSpeed is on.")]
    public float phase2AnimationSpeed = 5f;

    // Fires once, the moment the opening has played its last pass and the loop is about to begin. The
    // hook for anything that should start once the opening is over — a count-up, a sound, a button.
    // Also fires if the opening is skipped (phase1LoopCount 0, or phase2StartIndex 0), so a caller
    // waiting on it is never left waiting.
    public System.Action onPhase1Complete;

    private int currentPhase = 1;
    private int phase1CurrentLoop = 0;
    private int phase2CurrentLoop = 0;

    // phase2StartIndex clamped into the list, so a stale Inspector value after the frames are swapped
    // can never index outside it.
    private int Phase2Start => Mathf.Clamp(phase2StartIndex, 0, textureArray != null ? textureArray.Count : 0);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        EnsureRenderer();
        if (StartOnAwake)
        {
            StartAnimation();
        }
    }

    private void EnsureRenderer()
    {
        if (rendererDelegate == null)
        {
            rendererDelegate = GetComponent<Image>();
        }
    }

    void Start()
    {
        EnsureRenderer();
    }

    private void OnEnable()
    {
        EnsureRenderer();
        if (StartonEnable)
        {
            StartAnimation();
        }
    }

    private void OnDisable()
    {
        StopAnimation();
    }

    private void AnimationProcess()
    {
        if (textureArray == null || textureArray.Count == 0) return;

        SetTextureOfIndex();
        indexOfTexture++;

        if (animationMode == AnimationMode.TWO_PHASE)
        {
            AdvanceTwoPhase();
            return;
        }

        if (indexOfTexture >= textureArray.Count)
        {
            indexOfTexture = 0;
            currentLoopCount++;
            onLoopComplete?.Invoke(currentLoopCount);

            // onLoopComplete is where callers end the animation (StopAnimation cancels the pending
            // Invoke and clears the state). Without this check the loop below would immediately
            // schedule another Invoke, resurrecting an animation that was just stopped — and since
            // the state now reads NONE, no later StopAnimation could ever kill it again. That left
            // the animation writing over the icon's sprite forever, for the rest of the session.
            if (currentAnimationState != ImageState.PLAYING) return;

            if (doLoopAnimation)
            {
                Invoke(nameof(AnimationProcess), delayBetweenAnimation + delayBetweenLoop);
            }
            else
            {
                currentAnimationState = ImageState.NONE;
            }
        }
        else
        {
            Invoke(nameof(AnimationProcess), delayBetweenAnimation);
        }
    }

    // One step of a two-phase animation, called after the frame has been drawn and the index advanced.
    private void AdvanceTwoPhase()
    {
        int phaseEnd = currentPhase == 1 ? Phase2Start : textureArray.Count;

        // Still inside the current phase: just show the next frame.
        if (indexOfTexture < phaseEnd)
        {
            Invoke(nameof(AnimationProcess), delayBetweenAnimation);
            return;
        }

        // One full pass of the current phase has played.
        currentLoopCount++;
        onLoopComplete?.Invoke(currentLoopCount);
        if (!ShouldContinueAfterCallback()) return;

        if (currentPhase == 1)
        {
            phase1CurrentLoop++;

            if (phase1LoopCount < 0 || phase1CurrentLoop < phase1LoopCount)
            {
                indexOfTexture = 0;
                Invoke(nameof(AnimationProcess), delayBetweenAnimation + delayBetweenLoop);
                return;
            }

            EnterPhase2(scheduleNextFrame: true);
            return;
        }

        phase2CurrentLoop++;

        if (phase2LoopCount < 0 || phase2CurrentLoop < phase2LoopCount)
        {
            indexOfTexture = Phase2Start;
            Invoke(nameof(AnimationProcess), delayBetweenAnimation + delayBetweenLoop);
            return;
        }

        // The loop has played its count. The last frame drawn stays on screen, as with a
        // SINGLE_PHASE clip that has doLoopAnimation off.
        currentAnimationState = ImageState.NONE;
    }

    // Switches from the opening to the loop. The handover waits exactly one frame — no
    // delayBetweenLoop — so the opening runs straight into the loop with no visible pause.
    private void EnterPhase2(bool scheduleNextFrame)
    {
        currentPhase = 2;
        indexOfTexture = Phase2Start;
        CalculateFrameDelay();

        onPhase1Complete?.Invoke();
        if (!ShouldContinueAfterCallback()) return;

        // No loop to play: stop on the opening's last frame.
        if (phase2LoopCount == 0 || Phase2Start >= textureArray.Count)
        {
            currentAnimationState = ImageState.NONE;
            return;
        }

        if (scheduleNextFrame)
        {
            Invoke(nameof(AnimationProcess), delayBetweenAnimation);
        }
    }

    // After a callback, carry on only if the caller left the animation running and didn't restart it.
    //
    // The state check is the same guard as the SINGLE_PHASE resurrection fix. The IsInvoking check
    // covers a caller that called StartAnimation from inside the callback: that already scheduled the
    // next frame, so scheduling another here would run two frame chains and play at double speed.
    private bool ShouldContinueAfterCallback()
    {
        if (currentAnimationState != ImageState.PLAYING) return false;
        if (IsInvoking(nameof(AnimationProcess))) return false;
        return true;
    }

    // The per-frame delay. The frame count is the WHOLE list in both modes, so a two-phase clip runs at
    // the same per-frame pace as the same list would as one clip, and both phases share that pace unless
    // useCustomSpeed gives the loop its own. See the quadratic-timing note in GameExplanation.md §10.
    private void CalculateFrameDelay()
    {
        float speed = AnimationSpeed;
        if (animationMode == AnimationMode.TWO_PHASE && useCustomSpeed && currentPhase == 2)
        {
            speed = phase2AnimationSpeed;
        }

        delayBetweenAnimation = idealFrameRate * (float)textureArray.Count / speed;
        if (delayBetweenAnimation <= 0) delayBetweenAnimation = 0.05f;
    }

    public void StartAnimation()
    {
        if (textureArray == null || textureArray.Count == 0) return;

        EnsureRenderer();
        if (rendererDelegate == null) return;

        CancelInvoke(nameof(AnimationProcess));
        indexOfTexture = 0;
        currentLoopCount = 0;
        currentAnimationState = ImageState.PLAYING;

        RevertToInitialState();

        if (animationMode == AnimationMode.TWO_PHASE)
        {
            StartTwoPhase();
            return;
        }

        CalculateFrameDelay();
        Invoke(nameof(AnimationProcess), delayBetweenAnimation);
    }

    private void StartTwoPhase()
    {
        // Misconfiguration is still played sensibly (see the tooltips), but said out loud, because a
        // wrong split point is invisible in the Inspector and reads as "the animation is broken".
        if (phase2StartIndex <= 0 || phase2StartIndex >= textureArray.Count)
        {
            Debug.LogWarning($"[ImageAnimation] '{name}' is TWO_PHASE but phase2StartIndex is {phase2StartIndex} for {textureArray.Count} frames — " +
                             (phase2StartIndex <= 0 ? "there is no opening, only the loop will play." : "there is no loop, only the opening will play."));
        }

        CalculateFrameDelay();

        bool skipOpening = phase1LoopCount == 0 || Phase2Start == 0;
        if (!skipOpening)
        {
            Invoke(nameof(AnimationProcess), delayBetweenAnimation);
            return;
        }

        // Straight into the loop. Its first frame goes up immediately rather than one frame late.
        EnterPhase2(scheduleNextFrame: false);
        if (currentAnimationState != ImageState.PLAYING || IsInvoking(nameof(AnimationProcess))) return;

        SetTextureOfIndex();
        Invoke(nameof(AnimationProcess), delayBetweenAnimation);
    }

    public void PlayAnimation()
    {
        StartAnimation();
    }

    public void Play()
    {
        StartAnimation();
    }

    public void PauseAnimation()
    {
        if (currentAnimationState == ImageState.PLAYING)
        {
            CancelInvoke(nameof(AnimationProcess));
            currentAnimationState = ImageState.PAUSED;
        }
    }

    public void ResumeAnimation()
    {
        if (currentAnimationState == ImageState.PAUSED && !IsInvoking(nameof(AnimationProcess)))
        {
            Invoke(nameof(AnimationProcess), delayBetweenAnimation);
            currentAnimationState = ImageState.PLAYING;
        }
    }

    public void StopAnimation()
    {
        bool wasRunning = currentAnimationState != ImageState.NONE;

        // Cancel unconditionally. If the state and the pending Invoke ever disagree, that
        // mismatch is exactly what leaves an animation running with no way to stop it, so a
        // stop request must always clear the schedule regardless of what the state claims.
        CancelInvoke(nameof(AnimationProcess));
        currentAnimationState = ImageState.NONE;
        currentLoopCount = 0;
        ResetPhaseState();

        // The sprite revert stays conditional: KillWinTweens calls StopAnimation on every display
        // icon each spin, and writing textureArray[0] unconditionally would stamp a stale
        // animation frame over icons that were showing the correct result.
        if (wasRunning)
        {
            EnsureRenderer();
            if (rendererDelegate != null && textureArray != null && textureArray.Count > 0)
            {
                rendererDelegate.sprite = textureArray[0];
            }
        }
    }

    public void RevertToInitialState()
    {
        indexOfTexture = 0;
        ResetPhaseState();
        SetTextureOfIndex();
    }

    private void ResetPhaseState()
    {
        currentPhase = 1;
        phase1CurrentLoop = 0;
        phase2CurrentLoop = 0;
    }

    private void SetTextureOfIndex()
    {
        if (textureArray == null || textureArray.Count == 0 || indexOfTexture < 0 || indexOfTexture >= textureArray.Count) return;

        EnsureRenderer();
        if (rendererDelegate != null)
        {
            rendererDelegate.sprite = textureArray[indexOfTexture];
        }
    }
}
