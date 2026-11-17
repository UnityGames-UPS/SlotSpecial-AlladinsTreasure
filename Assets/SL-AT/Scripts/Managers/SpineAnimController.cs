using UnityEngine;
using Spine.Unity;


public class SpineAnimController : MonoBehaviour
{
    private SkeletonGraphic skeletonGraphic;
    [SerializeField] private string animName;
    private bool isPlaying;

    public SkeletonGraphic SkeletonGraphic
    {
        get
        {
            if (skeletonGraphic == null)
                skeletonGraphic = GetComponent<SkeletonGraphic>();
            return skeletonGraphic;
        }
    }



    void Awake()
    {
        skeletonGraphic = GetComponent<SkeletonGraphic>();
    }

    // ▶️ Play
    internal void Play(bool loop)
    {
        if (skeletonGraphic == null)
            skeletonGraphic = GetComponent<SkeletonGraphic>();

        if (skeletonGraphic != null && !string.IsNullOrEmpty(animName))
        {
            if (!isPlaying)
            {
                var track = skeletonGraphic.AnimationState.SetAnimation(0, animName, loop);
                if (track != null)
                {
                    track.TimeScale = 0.5f;
                }
                isPlaying = true;
            }
            skeletonGraphic.freeze = false;
        }
    }

    internal void Pause()
    {
        if (skeletonGraphic == null)
            skeletonGraphic = GetComponent<SkeletonGraphic>();

        if (skeletonGraphic != null)
        {
            if (skeletonGraphic.SkeletonData == null)
            {
                skeletonGraphic.Initialize(false);
            }
            skeletonGraphic.freeze = true;
        }
    }

    internal void Resume()
    {
        if (skeletonGraphic == null)
            skeletonGraphic = GetComponent<SkeletonGraphic>();

        if (skeletonGraphic != null)
        {
            if (skeletonGraphic.SkeletonData == null)
            {
                skeletonGraphic.Initialize(false);
            }

            if (!isPlaying && !string.IsNullOrEmpty(animName))
            {
                Play(true);
            }
            skeletonGraphic.freeze = false;
        }
    }

    // ⏹️ Stop (clears animation completely)
    internal void Stop()
    { 
        if(isPlaying)
        {
            var track = skeletonGraphic.AnimationState.GetCurrent(0);
            if (track != null)
            {
                track.TrackTime = track.Animation.Duration; // jump to last frame
                track.TimeScale = 0f;                       // freeze there
            }
            isPlaying = false;
        }
    }

    internal float GetAnimationDuration()
    {
        if (skeletonGraphic == null)
            skeletonGraphic = GetComponent<SkeletonGraphic>();

        if (skeletonGraphic != null)
        {
            if (skeletonGraphic.SkeletonData == null)
            {
                skeletonGraphic.Initialize(false);
            }
            if (skeletonGraphic.SkeletonData != null)
            {
                var anim = skeletonGraphic.SkeletonData.FindAnimation(animName);
                if (anim != null)
                {
                    return anim.Duration / 0.5f;
                }
            }
        }
        return 0f;
    }

    internal void SetSkeletonData(SkeletonDataAsset skeletonDataAsset, string overrideAnimName = null)
    {
        if (skeletonGraphic == null)
            skeletonGraphic = GetComponent<SkeletonGraphic>();

        if (skeletonGraphic != null)
        {
            string oldAnimName = animName;
            if (overrideAnimName != null)
            {
                animName = overrideAnimName;
            }

            if (skeletonGraphic.skeletonDataAsset != skeletonDataAsset)
            {
                skeletonGraphic.skeletonDataAsset = skeletonDataAsset;
                skeletonGraphic.Initialize(true);
                isPlaying = false;
            }
            
            // Check if the current animName is valid in the new skeleton data.
            // If it is not found, we fall back to the first animation in the new skeleton data.
            if (skeletonGraphic.SkeletonData != null)
            {
                if (string.IsNullOrEmpty(animName) || skeletonGraphic.SkeletonData.FindAnimation(animName) == null)
                {
                    var animations = skeletonGraphic.SkeletonData.Animations;
                    if (animations.Count > 0)
                    {
                        animName = animations.Items[0].Name;
                    }
                    else
                    {
                        animName = "";
                    }
                }
            }

            if (animName != oldAnimName)
            {
                isPlaying = false;
            }
        }
    }
}
