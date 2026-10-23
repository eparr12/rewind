namespace Rewind.Core.Scans;

/// <summary>
/// Factory barcodes in one form everywhere: a UPC-A is 12 digits and an EAN-13 is 13. The
/// phone catalog uses the same form, so a scan in a thrift store matches a case we own.
/// </summary>
public static class Upc
{
    private const int UpcALength = 12;
    private const int Ean13Length = 13;

    /// <summary>
    /// Checks a factory barcode and puts it in Rewind's form. A 13-digit EAN that starts with 0
    /// is the same product as its 12-digit UPC-A, so it becomes the 12-digit form.
    /// </summary>
    /// <returns>False when the code isn't 12 or 13 digits with a valid check digit.</returns>
    public static bool TryNormalize(string code, out string upc)
    {
        ArgumentNullException.ThrowIfNull(code);

        var normalized = code.Length == Ean13Length && code[0] == '0' ? code[1..] : code;
        if (normalized.Length is not (UpcALength or Ean13Length)
            || !normalized.All(char.IsAsciiDigit)
            || !CheckDigitIsValid(normalized))
        {
            upc = "";
            return false;
        }

        upc = normalized;
        return true;
    }

    // The GTIN rule: from the right, skipping the check digit itself, the weights go 3, 1, 3, 1…
    private static bool CheckDigitIsValid(string digits)
    {
        var sum = 0;
        for (var i = digits.Length - 2; i >= 0; i--)
        {
            var weight = (digits.Length - i) % 2 == 0 ? 3 : 1;
            sum += (digits[i] - '0') * weight;
        }

        return (10 - sum % 10) % 10 == digits[^1] - '0';
    }
}
