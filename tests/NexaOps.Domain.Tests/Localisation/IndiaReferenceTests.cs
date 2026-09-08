using NexaOps.Domain.Localisation;

namespace NexaOps.Domain.Tests.Localisation;

/// <summary>
/// Indian market reference data and format validation.
/// <para>
/// Every identifier used here is structurally valid but deliberately fictional. These are
/// format checks, not registry look-ups - the product does not claim to verify that a GSTIN
/// is actually registered.
/// </para>
/// </summary>
public sealed class IndiaReferenceTests
{
    [Fact]
    public void All_states_and_union_territories_are_present_and_unique()
    {
        IndiaReference.States.Count.ShouldBe(36);
        IndiaReference.States.Count(s => s.IsUnionTerritory).ShouldBe(8);

        IndiaReference.States.Select(s => s.Code).Distinct().Count().ShouldBe(36);
        IndiaReference.States.Select(s => s.GstStateCode).Distinct().Count().ShouldBe(36);
    }

    [Theory]
    [InlineData("KA", "Karnataka", "29")]
    [InlineData("MH", "Maharashtra", "27")]
    [InlineData("DL", "Delhi", "07")]
    [InlineData("TN", "Tamil Nadu", "33")]
    public void States_carry_the_correct_gst_code(string code, string name, string gstCode)
    {
        var state = IndiaReference.FindState(code);

        state.ShouldNotBeNull();
        state.Name.ShouldBe(name);
        state.GstStateCode.ShouldBe(gstCode);
    }

    [Fact]
    public void State_lookup_is_case_insensitive_and_tolerates_whitespace()
    {
        IndiaReference.FindState("ka")!.Code.ShouldBe("KA");
        IndiaReference.FindState(" MH ")!.Code.ShouldBe("MH");
        IndiaReference.FindState("ZZ").ShouldBeNull();
        IndiaReference.FindState(null).ShouldBeNull();
    }

    [Theory]
    [InlineData("29AACCA1234F1Z5")]   // Karnataka
    [InlineData("27AABCN5678M1Z2")]   // Maharashtra
    public void Structurally_valid_gstins_are_accepted(string gstin)
        => IndiaReference.IsStructurallyValidGstin(gstin).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("29AACCA1234F1Z")]     // too short
    [InlineData("29AACCA1234F1Z55")]   // too long
    [InlineData("99AACCA1234F1Z5")]    // no such state code
    [InlineData("29AACCA1234F1A5")]    // the fourteenth character must be Z
    [InlineData("291CCA1234F1Z5X")]    // malformed PAN section
    public void Malformed_gstins_are_rejected(string? gstin)
        => IndiaReference.IsStructurallyValidGstin(gstin).ShouldBeFalse();

    [Theory]
    [InlineData("AACCA1234F")]
    [InlineData("aabcn5678m")]
    public void Structurally_valid_pans_are_accepted(string pan)
        => IndiaReference.IsStructurallyValidPan(pan).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("AACCA1234")]     // too short
    [InlineData("AACC11234F")]    // digit in the letter block
    [InlineData("AACCA123AF")]    // letter in the digit block
    [InlineData("AACCA12345")]    // final character must be a letter
    public void Malformed_pans_are_rejected(string? pan)
        => IndiaReference.IsStructurallyValidPan(pan).ShouldBeFalse();

    [Theory]
    [InlineData("9876543210", "+919876543210")]
    [InlineData("09876543210", "+919876543210")]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("91-9876543210", "+919876543210")]
    [InlineData("(+91) 7012-345678", "+917012345678")]
    public void Indian_numbers_normalise_to_e164(string input, string expected)
        => IndiaReference.NormalisePhoneNumber(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]            // too short
    [InlineData("5876543210")]       // Indian mobile numbers never begin below 6
    [InlineData("98765432101234")]   // too long to interpret
    public void Numbers_that_are_not_indian_subscriber_numbers_return_null(string? input)
        => IndiaReference.NormalisePhoneNumber(input).ShouldBeNull();

    [Fact]
    public void Fixed_national_holidays_are_the_three_that_never_move()
    {
        var holidays = IndiaReference.FixedNationalHolidays;

        holidays.Count.ShouldBe(3);
        holidays.ShouldContain((1, 26, "Republic Day"));
        holidays.ShouldContain((8, 15, "Independence Day"));
        holidays.ShouldContain((10, 2, "Gandhi Jayanti"));
    }

    [Fact]
    public void Currency_formats_in_the_indian_numbering_system()
    {
        // Lakhs and crores group as 1,23,45,678 rather than 12,345,678.
        var formatted = IndiaReference.FormatCurrency(12345678.50m);

        formatted.ShouldContain("1,23,45,678");
    }

    [Fact]
    public void Product_defaults_target_the_indian_market()
    {
        IndiaReference.CountryCode.ShouldBe("IN");
        IndiaReference.CurrencyCode.ShouldBe("INR");
        IndiaReference.DefaultLocale.ShouldBe("en-IN");
        IndiaReference.DefaultDateFormat.ShouldBe("dd/MM/yyyy");
        IndiaReference.DefaultTimeZoneId.ShouldBe("India Standard Time");
    }
}
