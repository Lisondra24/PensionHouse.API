/// One place for the password rule, so the API, the web and the app all agree.
public static class PasswordRules
{
    public const int Min = 8;
    public const int Max = 32;

    // Returns an error message, or null when the password is fine
    public static string? Check(string? password)
    {
        var p = password ?? "";
        if (p.Length < Min) return $"The password must be at least {Min} characters.";
        if (p.Length > Max) return $"The password can be at most {Max} characters.";
        return null;
    }
}