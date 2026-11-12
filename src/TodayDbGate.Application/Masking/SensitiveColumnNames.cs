using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Application.Masking;

/// <summary>Suggests a masking action from a column name (English and Turkish names). Only a hint for the admin.</summary>
public static class SensitiveColumnNames
{
    private static readonly (string[] Contains, string[] Exact, MaskAction Action)[] Patterns =
    [
        (["password", "passwd", "secret", "apikey", "api_key", "token", "salt", "sifre", "parola"], ["hash", "pwd"], MaskAction.Redact),
        (["tckn", "tc_kimlik", "tckimlik", "kimlik_no", "kimlikno", "national_id", "identity_no", "ssn", "passport", "pasaport"], ["tc", "tc_no", "tcno"], MaskAction.Redact),
        (["card_number", "cardnumber", "credit_card", "creditcard", "kart_no", "kartno", "cvv", "cvc"], ["pan", "card", "kart"], MaskAction.Redact),
        (["birth", "dob", "dogum"], [], MaskAction.Redact),
        (["email", "e_mail", "eposta", "e_posta"], ["mail"], MaskAction.Partial),
        (["phone", "telefon", "mobile", "gsm", "cep_tel", "ceptel"], ["tel", "cep"], MaskAction.Partial),
        (["iban"], [], MaskAction.Partial),
        (["address", "adres", "street", "sokak", "postcode", "postal_code", "zip_code", "posta_kodu"], ["zip"], MaskAction.Partial),
        (["ip_address", "ipaddress"], [], MaskAction.Partial),
        (["full_name", "fullname", "first_name", "firstname", "last_name", "lastname", "surname", "ad_soyad", "adsoyad", "soyad", "musteri_adi", "customer_name"], ["adi", "isim"], MaskAction.Pseudonym),
    ];

    public static MaskAction? Suggest(string columnName)
    {
        var name = columnName.Trim().ToLowerInvariant();
        foreach (var (contains, exact, action) in Patterns)
        {
            if (exact.Contains(name) || contains.Any(name.Contains))
                return action;
        }
        return null;
    }
}
