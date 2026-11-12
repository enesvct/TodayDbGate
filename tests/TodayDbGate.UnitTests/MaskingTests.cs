using System.Security.Cryptography;
using TodayDbGate.Application.Masking;
using TodayDbGate.Application.Models;
using TodayDbGate.Domain.Entities;
using TodayDbGate.Domain.Enums;
using TodayDbGate.Infrastructure.Security;

namespace TodayDbGate.UnitTests;

public class PersonalDataDetectorTests
{
    [Theory]
    [InlineData("Contact ayse.kaya@example.com today", "Contact a***@example.com today")]
    [InlineData("TCKN: 10000000146", "TCKN: *********46")]
    [InlineData("IBAN TR33 0006 1005 1978 6457 8413 26", "IBAN TR** **** **** **** 1326")]
    [InlineData("card 4111 1111 1111 1111 ok", "card **** **** **** 1111 ok")]
    [InlineData("ara: 0532 123 45 67", "ara: *** *** ** 67")]
    [InlineData("+90 532 123 45 67", "*** *** ** 67")]
    [InlineData("5321234567", "*** *** ** 67")]
    public void Masks_personal_data_inside_text(string input, string expected) =>
        Assert.Equal(expected, PersonalDataDetector.MaskText(input));

    [Theory]
    [InlineData("12345678901")]          // 11 digits, bad TCKN checksum
    [InlineData("4111 1111 1111 1112")]  // fails Luhn
    [InlineData("TR33 0006 1005 1978 6457 8413 27")] // fails mod-97
    [InlineData("1234567890")]           // not a mobile number
    [InlineData("Order 2026-11-10, total 1499.90 TL")]
    public void Leaves_lookalikes_alone(string input) =>
        Assert.Same(input, PersonalDataDetector.MaskText(input));

    [Fact]
    public void Turkish_id_stored_as_number_is_masked()
    {
        Assert.Equal("*********46", PersonalDataDetector.Mask(10000000146L, out var changed));
        Assert.True(changed);
        Assert.Equal(42, PersonalDataDetector.Mask(42, out changed));
        Assert.False(changed);
    }
}

public class MaskingEngineTests
{
    private static readonly HmacPseudonymizer Pseudonymizer = new(RandomNumberGenerator.GetBytes(32));

    private static MaskingRule Rule(string table, string column, MaskAction action, string schema = "public") =>
        new() { SchemaName = schema, TableName = table, ColumnName = column, Action = action };

    private static QueryResult Result(string[] columns, ColumnOrigin?[] origins, params object?[][] rows) =>
        new(columns, rows, false, -1, 1, origins);

    private static readonly ColumnOrigin Email = new("public", "customers", "email");
    private static readonly ColumnOrigin FullName = new("public", "customers", "full_name");
    private static readonly ColumnOrigin City = new("public", "customers", "city");
    private static readonly ColumnOrigin Id = new("public", "customers", "id");

    [Fact]
    public void Column_rules_follow_the_origin_not_the_alias()
    {
        var policy = new MaskingPolicy(true, [Rule("customers", "email", MaskAction.Partial), Rule("customers", "full_name", MaskAction.Pseudonym)]);
        var result = Result(["contact", "who", "city", "note"], [Email, FullName, City, null],
            ["ali@example.com", "Ali Veli", "Ankara", "call 0532 123 45 67"],
            ["veli@example.com", "Ali Veli", "İzmir", "nothing here"]);

        var outcome = MaskingEngine.Apply(result, policy, Pseudonymizer);
        var rows = outcome.Result.Rows;

        Assert.Equal("a***@example.com", rows[0][0]);
        Assert.StartsWith("full_name_", (string)rows[0][1]!); // prefix from the real column, not the alias "who"
        Assert.Equal(rows[0][1], rows[1][1]);           // same name, same pseudonym
        Assert.Equal("Ankara", rows[0][2]);             // no rule, nothing personal
        Assert.Equal("call *** *** ** 67", rows[0][3]); // computed column: caught by detection
        Assert.Equal(["contact", "who", "note"], outcome.Result.MaskedColumns);
        Assert.Equal("ali@example.com", result.Rows[0][0]); // input untouched
    }

    [Fact]
    public void Table_redaction_with_column_exception()
    {
        var policy = new MaskingPolicy(false, [Rule("customers", "", MaskAction.Redact), Rule("customers", "id", MaskAction.Allow)]);
        var outcome = MaskingEngine.Apply(Result(["id", "email"], [Id, Email], [7, "a@b.co"]), policy, Pseudonymizer);

        Assert.Equal(7, outcome.Result.Rows[0][0]);
        Assert.Equal(MaskingEngine.Redacted, outcome.Result.Rows[0][1]);
    }

    [Fact]
    public void Blocked_table_is_reported()
    {
        var policy = new MaskingPolicy(false, [Rule("payments", "", MaskAction.Block)]);
        var outcome = MaskingEngine.Apply(
            Result(["id", "amount"], [Id, new ColumnOrigin("public", "payments", "amount")], [1, 10m]), policy, Pseudonymizer);

        Assert.Equal(["payments"], outcome.BlockedTables);
    }

    [Fact]
    public void Missing_schema_from_driver_still_matches()
    {
        var policy = new MaskingPolicy(false, [Rule("customers", "email", MaskAction.Redact, schema: "dbo")]);
        var outcome = MaskingEngine.Apply(
            Result(["email"], [new ColumnOrigin(null, "Customers", "EMAIL")], ["a@b.co"]), policy, Pseudonymizer);

        Assert.Equal(MaskingEngine.Redacted, outcome.Result.Rows[0][0]);
    }

    [Fact]
    public void Allow_exempts_a_column_from_detection_and_detection_can_be_off()
    {
        var allow = new MaskingPolicy(true, [Rule("customers", "email", MaskAction.Allow)]);
        Assert.Equal("a@b.co", MaskingEngine.Apply(Result(["email"], [Email], ["a@b.co"]), allow, Pseudonymizer).Result.Rows[0][0]);

        var off = new MaskingPolicy(false, []);
        Assert.Equal("a@b.co", MaskingEngine.Apply(Result(["x"], [null], ["a@b.co"]), off, Pseudonymizer).Result.Rows[0][0]);
    }

    [Theory]
    [InlineData("Ali Veli", "A*** V***")]
    [InlineData("0532 123 45 67", "*********67")]
    [InlineData("ali@example.com", "a***@example.com")]
    public void Partial_masks_keep_a_hint(string input, string expected) =>
        Assert.Equal(expected, MaskingEngine.MaskPartial(input));

    [Fact]
    public void Partial_hides_dates_and_keeps_last_digits_of_numbers()
    {
        Assert.Equal("***", MaskingEngine.MaskPartial(new DateTime(1990, 5, 1)));
        Assert.Equal("***45", MaskingEngine.MaskPartial(12345));
    }
}

public class PseudonymizerAndSuggestionTests
{
    [Fact]
    public void Pseudonyms_are_stable_keyed_and_case_insensitive_for_email()
    {
        var a = new HmacPseudonymizer(RandomNumberGenerator.GetBytes(32));
        var b = new HmacPseudonymizer(RandomNumberGenerator.GetBytes(32));

        // Same value -> same hash part, whatever the column; e-mail case does not matter.
        Assert.Equal(a.Pseudonymize("email", "Ali@Example.com")[^10..], a.Pseudonymize("contact", "ali@example.com")[^10..]);
        Assert.Equal(a.Pseudonymize("email", "x@y.co"), a.Pseudonymize("email", "x@y.co"));
        Assert.NotEqual(a.Pseudonymize("email", "x@y.co"), b.Pseudonymize("email", "x@y.co"));
        Assert.Matches("^customer_name_[0-9a-f]{10}$", a.Pseudonymize("Customer Name", "Ali"));
    }

    [Theory]
    [InlineData("email", MaskAction.Partial)]
    [InlineData("customer_email", MaskAction.Partial)]
    [InlineData("tc_kimlik_no", MaskAction.Redact)]
    [InlineData("password_hash", MaskAction.Redact)]
    [InlineData("full_name", MaskAction.Pseudonym)]
    [InlineData("cep_telefonu", MaskAction.Partial)]
    [InlineData("city", null)]
    [InlineData("name", null)]
    public void Column_names_suggest_an_action(string column, MaskAction? expected) =>
        Assert.Equal(expected, SensitiveColumnNames.Suggest(column));
}
