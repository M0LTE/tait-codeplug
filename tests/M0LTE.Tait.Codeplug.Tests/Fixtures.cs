using M0LTE.Tait.Codeplug;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary>
/// Real codeplug blocks the profile and Programmable-I/O tests build on: a factory-default TM8110
/// digital line table (DBVer 0094, every line unassigned) and the TARPN TM8105 programming template
/// (DBVer 0095), a CPS save with AUX_GPI1 = Input / External PTT 1, IOP_GPIO2 = Output / Busy Status
/// and IOP_GPIO4 = Input / Unmute Audio Output Path with action parameters set.
/// </summary>
internal static class Fixtures
{
    public const string DefaultDigitalIoTable =
        "960134E9FCC664A8000000000000C055004D3ABF3554000000000000002B80269D5F1A2A00000000000090194093CE6F0C860A0000000000006805D0A4F32B4305000000000000B60268D2F9CDA1020000000000005C0134E9FCC65001000000000080AE009A747E73A80000000000008067004D3ABF31182A000000000000F0194093CE6F2C860A0000000000008006D0A4F31B93A102000000000000A10134E9FCC666A80000000000008068004D3ABF311A2A000000000000301A4093CE6FAC860A0000000000009006C32FB21824A202000000000000";

    /// <summary>The default table with AUX_GPI1 programmed Input / External PTT 1 / active Low, as
    /// the CPS saves it - the aux-connector packet wiring, and nothing else changed.</summary>
    public const string PacketDigitalIoTable =
        "960134E9FCC664A1204900000000C055004D3ABF3554000000000000002B80269D5F1A2A00000000000090194093CE6F0C860A0000000000006805D0A4F32B4305000000000000B60268D2F9CDA1020000000000005C0134E9FCC65001000000000080AE009A747E73A80000000000008067004D3ABF31182A000000000000F0194093CE6F2C860A0000000000008006D0A4F31B93A102000000000000A10134E9FCC666A80000000000008068004D3ABF311A2A000000000000301A4093CE6FAC860A0000000000009006C32FB21824A202000000000000";

    public const string TarpnDigitalIoTable =
        "960134E9FCC664A1204900000000C055004D3ABF3554000000000000002B80269D5F1A2A00000000000090194093CE6F0C860A0000000000006805D0A4F32B4305000000000000B60268D2F9CDA1020000000000005C0134E9FCC65001000000000080AE009A747E73A80000000000008067004D3ABF319828000000000000F0194093CE6F2C860A0000000000008006D0A4F31B938502820002000200A10134E9FCC666A80000000000008068004D3ABF311A2A000000000000301A4093CE6FAC860A0000000000009006C32FB21824A202000000000000";

    // The three Programmable I/O records as a factory-default TM8100 (DBVer 0095) holds them, and as
    // the CPS saves them once the aux-connector packet wiring is set by hand. The pair is a matched
    // before/after of exactly one CPS edit - External PTT 1 to Data / Audio Tap In, AUX_GPI1 to
    // Input / External PTT 1 / active Low, Rx tap-out R1 unmuted Except on PTT, EPTT1 tap-in T13 -
    // and no other record in the file differs, which is what makes it a byte-exact target.

    /// <summary>PTT table (0x19): three 31-bit entries, all Voice / AUX MIC.</summary>
    public const string DefaultPttTable = "14CC00000A96000005630000";

    /// <summary>PTT table with External PTT 1 transmitting Data from the Audio Tap In.</summary>
    public const string PacketPttTable = "14CC00000A9A000005630000";

    /// <summary>Audio block (0x3B): Rx tap-out None unmuted On PTT, EPTT1 tap-in None.</summary>
    public const string DefaultAudioBlock = "000100C000800000400080000020004000001000";

    /// <summary>Audio block with Rx tap-out R1 / Split / Except on PTT and EPTT1 tap-in T13.</summary>
    public const string PacketAudioBlock = "000100C1088000004000003A0020004000001000";

    /// <summary>The same block with the tap-out point moved to R2 for the internal options board.</summary>
    public const string PacketAudioBlockR2 = "000100C2088000004000003A0020004000001000";

    // The three Key Settings records as a factory-default TM8100 (DBVer 0094) holds them, and as the
    // CPS saves them once F1 alone is set to Squelch Override. Another matched before/after of exactly
    // one CPS edit: those two codeplugs differ in these three records and nothing else.

    /// <summary>Key table (0x0F): four 20-bit entries, one per front-panel key, all unassigned.</summary>
    public const string DefaultFunctionKeyTable = "00000000000000000000";

    /// <summary>Key table with F1 - the first entry - programmed to Squelch Override.</summary>
    public const string SquelchOverrideFunctionKeyTable = "00040000000000000000";

    /// <summary>Function table (0x03): 65 14-bit entries, one per assignable function. Squelch
    /// Override is entry 43, here 0x2000 - not in use by any key.</summary>
    public const string DefaultFunctionTable =
        "83142034C808228104002040040002C000204080002004080240002002080010000C00000C0004400160200C0804428160C82032080002802020080800828000200808020280002000080002802020080802828003000820400810200C0804428160000C0004400160002000080002800104";

    /// <summary>The same table with Squelch Override's entry marked in use by a key (0x2000 -&gt; 0x0C00).</summary>
    public const string SquelchOverrideFunctionTable =
        "83142034C808228104002040040002C000204080002004080240002002080010000C00000C0004400160200C0804428160C82032080002802020080800828000200808020280002000080002302020080802828003000820400810200C0804428160000C0004400160002000080002800104";

    /// <summary>Programmed-key list (0x18) with Squelch Override's single 22-bit entry. A default
    /// codeplug carries no 0x18 record at all and an item count of zero.</summary>
    public const string SquelchOverrideKeyList = "100000";

    // Item index entries (7 bytes each) for the items the profiles touch: the function table
    // (0x03, 14 bits x 65), the key table (0x0F, 20 bits x 4), the programmed-key list (0x18, 22 bits
    // x 0), the audio block (0x3B, 95 bits x 4) and the digital line table (0x37, 132 bits x 15), as a
    // real readout has them.
    private const string ItemIndex =
        "030E0041000200" + "0F140004000100" + "18160000000300" + "3B5F0004000900" + "3784000F000600";

    /// <summary>A codeplug carrying every block the PDN profiles write: the data/signalling record,
    /// the item index, the PTT table, the audio block, the digital I/O line table, the function table
    /// and the front-panel key table. Every block but the data record is the real factory-default one.</summary>
    public static CodeplugFields Open(string digitalIoTableHex)
    {
        var image = new CodeplugImage(
            [new KeyValuePair<string, string>("DBVer", "0095")],
            [
                new CodeplugRecord(0x01, 0, Convert.FromHexString(ItemIndex)),
                new CodeplugRecord(0x09, 0, new byte[37]),
                new CodeplugRecord(0x0F, 0, Convert.FromHexString(DefaultFunctionKeyTable)),
                new CodeplugRecord(0x19, 0, Convert.FromHexString(DefaultPttTable)),
                new CodeplugRecord(0x3B, 0, Convert.FromHexString(DefaultAudioBlock)),
            ]);
        image.SetSectionBytes(0x03, Convert.FromHexString(DefaultFunctionTable));
        image.SetSectionBytes(0x37, Convert.FromHexString(digitalIoTableHex));
        return CodeplugFields.Open(image);
    }
}
