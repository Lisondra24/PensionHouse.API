public static class PhilippineIds
{
    public static readonly string[] Types =
    {
        "Philippine National ID (PhilSys)",
        "Passport",
        "Driver's License",
        "UMID",
        "SSS ID",
        "GSIS ID",
        "PRC ID",
        "Postal ID",
        "Voter's ID",
        "PhilHealth ID",
        "TIN ID",
        "Pag-IBIG ID",
        "Senior Citizen ID",
        "PWD ID",
        "OFW / OWWA ID",
        "NBI Clearance",
        "Police Clearance"
    };

    public static bool IsValid(string? value) => value is not null && Types.Contains(value);
}