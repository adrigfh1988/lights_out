using UnityEngine;

/// <summary>
/// F82: the pool of lines Stan says in speech bubbles, one array per situation (StalkerVoice picks from
/// them with a per-category shuffle bag). Edit the asset (Assets/SourceFiles/Data/StanPhrases.asset, made
/// by LIGHTS OUT &gt; Build Phrase Books) in the Inspector; "Reset" from the component menu restores
/// the defaults. An empty category is legal and just means Stan says nothing for that event. The token
/// {floor} in a line is replaced with the current floor number.
/// </summary>
[CreateAssetMenu(menuName = "LIGHTS OUT/Stan Phrase Book")]
public class StalkerPhraseBook : ScriptableObject
{
    public enum Category { Spotted, Lit, Staring, Lunge, Touch, Lost, Locker, Idle }

    [Tooltip("Sight acquired after a while without it.")]
    [TextArea(1, 2)] public string[] spotted;
    [Tooltip("The torch just froze him while he can see you.")]
    [TextArea(1, 2)] public string[] lit;
    [Tooltip("He stops dead and stares (the Watch beat).")]
    [TextArea(1, 2)] public string[] staring;
    [Tooltip("Shouted: he rushes you.")]
    [TextArea(1, 2)] public string[] lunge;
    [Tooltip("Shouted: he touches you, just before he vanishes.")]
    [TextArea(1, 2)] public string[] touch;
    [Tooltip("He loses sight of you after watching for a while.")]
    [TextArea(1, 2)] public string[] lost;
    [Tooltip("You are hiding in a locker and he is close.")]
    [TextArea(1, 2)] public string[] locker;
    [Tooltip("He has been watching you for a long time.")]
    [TextArea(1, 2)] public string[] idle;

    public string[] Get(Category category)
    {
        switch (category)
        {
            case Category.Spotted: return spotted;
            case Category.Lit: return lit;
            case Category.Staring: return staring;
            case Category.Lunge: return lunge;
            case Category.Touch: return touch;
            case Category.Lost: return lost;
            case Category.Locker: return locker;
            default: return idle;
        }
    }

    /// <summary>An in-memory book with the default lines - StalkerVoice's fallback when the asset is not assigned.</summary>
    public static StalkerPhraseBook CreateDefault()
    {
        StalkerPhraseBook book = CreateInstance<StalkerPhraseBook>();
        book.FillDefaults();
        return book;
    }

    /// <summary>Voice: a playful, childlike, broken machine. Short lines so they type fast and fit one or two rows.</summary>
    public void FillDefaults()
    {
        spotted = new[]
        {
            "there you are.", "i see you.", "found you.", "hello again.", "peekaboo.",
            "don't run. please run.", "you look tired.", "i was waiting.",
        };
        lit = new[]
        {
            "too bright.", "you can't keep it on forever.", "click. click.", "blink.", "i'll wait.",
            "your battery is dying.", "statue game?", "don't look away.",
        };
        staring = new[]
        {
            "stay still.", "one… two…", "shhh.", "let me look at you.", "closer.", "don't move.",
        };
        lunge = new[] { "BOO.", "TAG!", "GOT YOU—", "MINE.", "NOW." };
        touch = new[]
        {
            "tag. you're it.", "warm.", "he heard that.", "i told him where you are.", "your light tastes good.",
        };
        lost = new[]
        {
            "where did you go?", "hide and seek, then.", "i can hear you breathing.", "you'll come back.",
            "ready or not…",
        };
        locker = new[]
        {
            "knock knock.", "i know which one.", "it's cramped in there, isn't it?", "come out, come out.",
            "i can wait all night.",
        };
        idle = new[]
        {
            "do you like the maze?", "he's closer than me.", "floor {floor}. not many left.", "your heart is loud.",
            "the stars aren't for you.", "keep walking.",
        };
    }

    private void Reset() => FillDefaults();
}
