namespace NexaOps.Domain.Localisation;

/// <summary>
/// An Indian state or union territory, with the GST state code used on tax documents.
/// Held in the domain rather than a database table because the list changes on the order of
/// once a decade, and a lookup that cannot fail is worth more than one that is configurable.
/// </summary>
public sealed record IndianState(string Code, string Name, string GstStateCode, bool IsUnionTerritory);

/// <summary>Reference data for the Indian market: states, phone format, currency, holidays.</summary>
public static class IndiaReference
{
    public const string CountryCode = "IN";
    public const string CurrencyCode = "INR";
    public const string CurrencySymbol = "₹";
    public const string DefaultTimeZoneId = "India Standard Time";
    public const string DefaultLocale = "en-IN";

    /// <summary>Day-first, the convention in Indian business correspondence.</summary>
    public const string DefaultDateFormat = "dd/MM/yyyy";

    /// <summary>All 28 states and 8 union territories, with GST state codes.</summary>
    public static readonly IReadOnlyList<IndianState> States =
    [
        new("AN", "Andaman and Nicobar Islands", "35", true),
        new("AP", "Andhra Pradesh", "37", false),
        new("AR", "Arunachal Pradesh", "12", false),
        new("AS", "Assam", "18", false),
        new("BR", "Bihar", "10", false),
        new("CH", "Chandigarh", "04", true),
        new("CG", "Chhattisgarh", "22", false),
        new("DH", "Dadra and Nagar Haveli and Daman and Diu", "26", true),
        new("DL", "Delhi", "07", true),
        new("GA", "Goa", "30", false),
        new("GJ", "Gujarat", "24", false),
        new("HR", "Haryana", "06", false),
        new("HP", "Himachal Pradesh", "02", false),
        new("JK", "Jammu and Kashmir", "01", true),
        new("JH", "Jharkhand", "20", false),
        new("KA", "Karnataka", "29", false),
        new("KL", "Kerala", "32", false),
        new("LA", "Ladakh", "38", true),
        new("LD", "Lakshadweep", "31", true),
        new("MP", "Madhya Pradesh", "23", false),
        new("MH", "Maharashtra", "27", false),
        new("MN", "Manipur", "14", false),
        new("ML", "Meghalaya", "17", false),
        new("MZ", "Mizoram", "15", false),
        new("NL", "Nagaland", "13", false),
        new("OD", "Odisha", "21", false),
        new("PY", "Puducherry", "34", true),
        new("PB", "Punjab", "03", false),
        new("RJ", "Rajasthan", "08", false),
        new("SK", "Sikkim", "11", false),
        new("TN", "Tamil Nadu", "33", false),
        new("TS", "Telangana", "36", false),
        new("TR", "Tripura", "16", false),
        new("UP", "Uttar Pradesh", "09", false),
        new("UK", "Uttarakhand", "05", false),
        new("WB", "West Bengal", "19", false)
    ];

    public static IndianState? FindState(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? null
            : States.FirstOrDefault(s => string.Equals(s.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Fixed-date national holidays. Festival dates that follow the lunar calendar (Diwali,
    /// Holi, Eid) move every year and are therefore seeded per year against a tenant calendar
    /// rather than hard-coded here - claiming to know next year's Diwali date in code would be
    /// wrong more often than it is right.
    /// </summary>
    public static readonly IReadOnlyList<(int Month, int Day, string Name)> FixedNationalHolidays =
    [
        (1, 26, "Republic Day"),
        (8, 15, "Independence Day"),
        (10, 2, "Gandhi Jayanti")
    ];

    /// <summary>
    /// Validates a GSTIN structurally: 2-digit state code, 10-character PAN, entity digit,
    /// the letter Z, and a checksum character. This is a format check, not a registry lookup.
    /// </summary>
    public static bool IsStructurallyValidGstin(string? gstin)
    {
        if (string.IsNullOrWhiteSpace(gstin) || gstin.Length != 15)
        {
            return false;
        }

        var value = gstin.Trim().ToUpperInvariant();

        if (!char.IsAsciiDigit(value[0]) || !char.IsAsciiDigit(value[1]))
        {
            return false;
        }

        var stateCode = value[..2];
        if (!States.Any(s => s.GstStateCode == stateCode))
        {
            return false;
        }

        return IsStructurallyValidPan(value.Substring(2, 10))
               && char.IsAsciiLetterOrDigit(value[12])
               && value[13] == 'Z'
               && char.IsAsciiLetterOrDigit(value[14]);
    }

    /// <summary>Validates a PAN structurally: five letters, four digits, one letter.</summary>
    public static bool IsStructurallyValidPan(string? pan)
    {
        if (string.IsNullOrWhiteSpace(pan) || pan.Length != 10)
        {
            return false;
        }

        var value = pan.Trim().ToUpperInvariant();

        for (var i = 0; i < 5; i++)
        {
            if (!char.IsAsciiLetterUpper(value[i]))
            {
                return false;
            }
        }

        for (var i = 5; i < 9; i++)
        {
            if (!char.IsAsciiDigit(value[i]))
            {
                return false;
            }
        }

        return char.IsAsciiLetterUpper(value[9]);
    }

    /// <summary>
    /// Normalises an Indian mobile or landline number to E.164. Accepts the forms people
    /// actually type: 9876543210, 09876543210, +91 98765 43210, 91-9876543210.
    /// Returns null when the input cannot be interpreted as an Indian number.
    /// </summary>
    public static string? NormalisePhoneNumber(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());

        // Strip a country code or trunk prefix if present.
        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }
        else if (digits.Length == 11 && digits[0] == '0')
        {
            digits = digits[1..];
        }

        // Indian subscriber numbers are ten digits and never begin 0-5.
        if (digits.Length != 10 || digits[0] < '6')
        {
            return null;
        }

        return "+91" + digits;
    }

    /// <summary>Formats an amount in the Indian numbering system, e.g. 1,23,45,678.00.</summary>
    public static string FormatCurrency(decimal amount)
        => string.Format(
            System.Globalization.CultureInfo.GetCultureInfo(DefaultLocale),
            "{0:C}",
            amount);
}
