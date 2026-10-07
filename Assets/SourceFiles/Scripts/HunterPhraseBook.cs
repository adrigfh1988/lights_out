using UnityEngine;

/// <summary>
/// F82: the pool of lines the hunter says in speech bubbles, one array per situation
/// (HunterVoice picks from them with a per-category shuffle bag). Edit the asset
/// (Assets/SourceFiles/Data/HunterPhrases.asset, made by LIGHTS OUT &gt; Build &gt; Phrase Books) in the
/// Inspector; "Reset" from the component menu restores the defaults. An empty category is legal and just
/// means it says nothing for that event. The token {floor} is replaced with the current floor number.
/// </summary>
[CreateAssetMenu(menuName = "LIGHTS OUT/Hunter Phrase Book")]
public class HunterPhraseBook : ScriptableObject
{
    public enum Category { Spotted, Chasing, Lost, Searching, Ambush, Locker, Door, Caught }

    [Tooltip("It sees you after a while without seeing you.")]
    [TextArea(1, 2)] public string[] spotted;
    [Tooltip("A long chase with you in sight.")]
    [TextArea(1, 2)] public string[] chasing;
    [Tooltip("It lost you mid-chase and starts searching.")]
    [TextArea(1, 2)] public string[] lost;
    [Tooltip("Muttered while searching nearby without seeing you.")]
    [TextArea(1, 2)] public string[] searching;
    [Tooltip("Shouted: it springs out of an ambush with you in sight.")]
    [TextArea(1, 2)] public string[] ambush;
    [Tooltip("You are hiding in a locker and it is close.")]
    [TextArea(1, 2)] public string[] locker;
    [Tooltip("It just bashed a shutter door open.")]
    [TextArea(1, 2)] public string[] door;
    [Tooltip("Shouted: it caught you (plays into the capture blackout).")]
    [TextArea(1, 2)] public string[] caught;

    public string[] Get(Category category)
    {
        switch (category)
        {
            case Category.Spotted: return spotted;
            case Category.Chasing: return chasing;
            case Category.Lost: return lost;
            case Category.Searching: return searching;
            case Category.Ambush: return ambush;
            case Category.Locker: return locker;
            case Category.Door: return door;
            default: return caught;
        }
    }

    /// <summary>An in-memory book with the default lines - HunterVoice's fallback when the asset is not assigned.</summary>
    public static HunterPhraseBook CreateDefault()
    {
        HunterPhraseBook book = CreateInstance<HunterPhraseBook>();
        book.FillDefaults();
        return book;
    }

    /// <summary>Voice: slow, patient, certain - an adult predator, where Stan is a broken toy.</summary>
    public void FillDefaults()
    {
        spotted = new[]
        {
            "i see you now.", "there's my little light.", "run.", "you're mine.", "too slow.",
            "i smell your battery.", "hello, thief.",
        };
        chasing = new[]
        {
            "run faster.", "your legs are tired.", "i don't get tired.", "closer…",
            "the stars won't save you.", "keep running. i like it.",
        };
        lost = new[]
        {
            "you can't hide forever.", "i'll find you.", "i know this maze better.", "hiding? good.",
        };
        searching = new[]
        {
            "where are you…", "come out.", "i can hear your heart.", "little light, little light…",
            "the dark is mine.", "floor {floor}. you won't leave it.",
        };
        ambush = new[] { "SURPRISE.", "WAITED SO LONG.", "THIS WAY? GOOD.", "HELLO." };
        locker = new[]
        {
            "is someone in there?", "i smell you.", "open up.", "one of these…", "warm metal.",
        };
        door = new[] { "doors don't stop me.", "let me in.", "knock knock.", "no more doors." };
        caught = new[] { "GOTCHA.", "MINE.", "LIGHTS OUT.", "FOUND YOU." };
    }

    private void Reset() => FillDefaults();
}
