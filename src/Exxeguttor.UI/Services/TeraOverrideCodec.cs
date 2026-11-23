namespace Exxeguttor.UI.Services;

/// <summary>Traduce entre el índice del combo "Override" del tab Special y el byte real de
/// <c>PK9.TeraTypeOverride</c>. Combo: 0 = "Sin override" (19), 1..18 = los 18 tipos (0..17),
/// 19 = Stellar (99). Valores de TeraTypeUtil en PKHeX.Core: OverrideNone = 19, Stellar = 99.</summary>
public static class TeraOverrideCodec
{
    public const byte OverrideNone = 19;
    public const byte Stellar      = 99;
    public const int  StellarIndex = 19;

    public static byte ToCode(int index) =>
        index <= 0 ? OverrideNone
        : index <= 18 ? (byte)(index - 1)
        : Stellar;

    public static int ToIndex(byte code) =>
        code == OverrideNone ? 0
        : code <= 17 ? code + 1
        : code == Stellar ? StellarIndex
        : 0;
}
