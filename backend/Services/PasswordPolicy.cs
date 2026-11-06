using System.Globalization;
using System.Text;

namespace ExpenseTracker.Api.Services;

// Rules for every new password (sign-up, reset, change), after NIST SP 800-63B and OWASP ASVS: a minimum length,
// a block list of common, repetitive and context words, and a breach check. No "must contain a symbol" rules (NIST
// advises against them); the app shows a strength meter instead. Sign-in never applies these rules, so older
// passwords keep working.
public class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public const string TooShort = "Use at least 8 characters.";
    public const string TooLong = "Use 128 characters or fewer.";
    public const string HasSpaces = "Spaces aren't allowed in passwords.";
    public const string TooCommon = "This password is too common — pick something less guessable.";
    public const string HasEmailName = "Don't use your email name in your password.";
    public const string Breached = "This password has appeared in a data breach. Choose a different one.";

    private readonly IBreachedPasswordChecker _breaches;

    public PasswordPolicy(IBreachedPasswordChecker breaches) => _breaches = breaches;

    // What's wrong with a new password, in words for the user, or null when it's fine.
    public async Task<string?> CheckAsync(string? password, string? email, CancellationToken ct = default)
    {
        var problem = Check(password, email);
        if (problem != null) return problem;
        return await _breaches.IsBreachedAsync(password!, ct) ? Breached : null;
    }

    // Everything except the breach check, which needs the network.
    public static string? Check(string? password, string? email)
    {
        password ??= string.Empty;
        if (password.Any(char.IsWhiteSpace)) return HasSpaces;
        // Counted in characters as people see them, so an emoji or an Urdu letter with marks counts once.
        var length = new StringInfo(password).LengthInTextElements;
        if (length < MinLength) return TooShort;
        if (length > MaxLength) return TooLong;

        var lower = password.ToLowerInvariant();
        var emailName = email?.Split('@')[0].ToLowerInvariant() ?? string.Empty;
        if (emailName.Length >= 4 && lower.Contains(emailName)) return HasEmailName;
        if (lower.Contains(AppWord) || IsGuessable(lower)) return TooCommon;
        return null;
    }

    private const string AppWord = "expense";

    private static bool IsGuessable(string lower)
    {
        if (IsPattern(lower) || CommonPasswords.Contains(lower) || CommonPasswords.Contains(Unleet(lower))) return true;

        // A common word dressed up with digits or symbols ("Pakistan@123", "monkey!!"), unless the extra part is long
        // and random enough on its own ("ali90517386").
        var start = 0;
        var end = lower.Length;
        while (start < end && !char.IsLetter(lower[start])) start++;
        while (end > start && !char.IsLetter(lower[end - 1])) end--;
        var core = lower[start..end];
        var rest = lower[..start] + lower[end..];
        if (core.Length < 3 || rest.Length == 0) return false;
        var commonCore = CommonPasswords.Contains(core) || CommonPasswords.Contains(Unleet(core));
        return commonCore && (rest.Length <= core.Length || IsPattern(rest));
    }

    // Repeats ("aaaaaaaa", "abcabcab"), runs ("12345678", "hgfedcba") and keyboard rows ("qwertyui", "1qaz2wsx").
    private static bool IsPattern(string s)
    {
        if (s.Distinct().Count() < 3) return true;
        for (var block = 1; block <= s.Length / 2; block++)
        {
            var repeats = true;
            for (var i = block; i < s.Length && repeats; i++) repeats = s[i] == s[i % block];
            if (repeats) return true;
        }
        return Sequences.Any(seq => seq.Contains(s));
    }

    private static readonly string[] Rows =
    {
        "abcdefghijklmnopqrstuvwxyz", "01234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm", "qwertyuiopasdfghjklzxcvbnm",
        "1qaz2wsx3edc4rfv5tgb6yhn7ujm8ik9ol0p", "qazwsxedcrfvtgbyhnujmikolp", "1q2w3e4r5t6y7u8i9o0p", "!@#$%^&*()",
    };

    private static readonly string[] Sequences = Rows.Concat(Rows.Select(r => new string(r.Reverse().ToArray()))).ToArray();

    // "p@ssw0rd" → "password".
    private static string Unleet(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c switch { '@' or '4' => 'a', '0' => 'o', '3' => 'e', '1' or '!' => 'i', '$' or '5' => 's', '7' => 't', _ => c });
        return sb.ToString();
    }

    // The most used passwords in public breach lists, plus local favourites. Short entries matter because of the
    // "common word plus digits" rule above.
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.Ordinal)
    {
        "123456", "password", "12345678", "qwerty", "123456789", "12345", "1234", "111111", "1234567", "dragon",
        "123123", "baseball", "abc123", "football", "monkey", "letmein", "696969", "shadow", "master", "666666",
        "qwertyuiop", "123321", "mustang", "1234567890", "michael", "654321", "superman", "1qaz2wsx", "7777777", "121212",
        "000000", "qazwsx", "123qwe", "killer", "trustno1", "jordan", "jennifer", "zxcvbnm", "asdfgh", "hunter",
        "buster", "soccer", "harley", "batman", "andrew", "tigger", "sunshine", "iloveyou", "charlie", "robert",
        "thomas", "hockey", "ranger", "daniel", "starwars", "112233", "george", "computer", "michelle", "jessica",
        "pepper", "zxcvbn", "555555", "11111111", "131313", "freedom", "777777", "pass", "maggie", "159753",
        "aaaaaa", "ginger", "princess", "joshua", "cheese", "amanda", "summer", "love", "ashley", "nicole",
        "chelsea", "matthew", "access", "yankees", "987654321", "dallas", "austin", "thunder", "taylor", "matrix",
        "william", "corvette", "hello", "martin", "heather", "secret", "merlin", "diamond", "1234qwer", "hammer",
        "silver", "222222", "88888888", "anthony", "justin", "test", "bailey", "q1w2e3r4t5", "patrick", "internet",
        "scooter", "orange", "golfer", "cookie", "richard", "samantha", "guitar", "jackson", "whatever", "mickey",
        "chicken", "sparky", "snoopy", "maverick", "phoenix", "peanut", "morgan", "welcome", "falcon", "cowboy",
        "ferrari", "samsung", "smokey", "joseph", "mercedes", "arsenal", "eagles", "melissa", "monster", "yellow",
        "123123123", "qwer1234", "purple", "banana", "junior", "hannah", "123654", "money", "london", "tennis",
        "999999", "coffee", "q1w2e3r4", "mother", "forever", "333333", "oliver", "player", "knight", "midnight",
        "please", "angel", "flower", "wizard", "rachel", "winner", "adidas", "victoria", "1q2w3e4r", "jasmine",
        "winter", "prince", "cocacola", "888888", "asdfasdf", "crystal", "87654321", "12344321", "golden", "madison",
        "apple", "password1", "password12", "password123", "passw0rd", "p@ssw0rd", "admin", "admin123", "administrator", "welcome1",
        "welcome123", "qwerty123", "qwerty1", "1q2w3e", "1q2w3e4r5t", "zaq12wsx", "abcd1234", "a1b2c3d4", "aa123456", "abc12345",
        "iloveyou1", "login", "default", "guest", "changeme", "secret123", "test123", "test1234", "00000000", "12341234",
        "11223344", "147258369", "1qazxsw2", "asdf1234", "zxcv1234", "qwe123", "123qweasd", "qweasdzxc", "qwertyui", "asdfghjk",
        "myspace", "family", "friends", "lovely", "letmein1", "superman1", "iloveu", "babygirl", "computer1", "football1",
        "pakistan", "pakistani", "pakistan123", "pakistan1947", "karachi", "lahore", "islamabad", "peshawar", "quetta", "multan",
        "faisalabad", "rawalpindi", "allah", "allah786", "786786", "bismillah", "muhammad", "mohammad", "mohammed", "ahmed",
        "hassan", "hussain", "imran", "imrankhan", "cricket", "jannat", "zindabad", "pakistanzindabad", "babar", "shahid",
    };
}
