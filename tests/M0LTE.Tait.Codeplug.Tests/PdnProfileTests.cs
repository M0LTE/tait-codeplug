using AwesomeAssertions;
using M0LTE.Tait.Codeplug;
using Xunit;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary>
/// The upgrade profiles - <c>audio-and-ptt</c> and the three PDN ones - against a codeplug carrying
/// every block they write (see <see cref="Fixtures"/>). Each profile is checked for what it turns on,
/// what it leaves alone, and - for the audio block - byte for byte against the CPS's own save of that
/// configuration.
/// </summary>
public class PdnProfileTests
{
    private const string PacketAudioBlock = Fixtures.PacketAudioBlock;

    private const string PacketAudioBlockR2 = Fixtures.PacketAudioBlockR2;

    [Fact]
    public void Audio_and_ptt_profile_wires_the_aux_connector_and_nothing_else()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);
        f.DataPort = DataPort.Aux;

        f.ApplyAudioAndPtt();

        AudioAndPttAreWiredForTheAuxConnector(f);
        // on its own it is the I/O forms only: the data path is left exactly as it was found - including
        // the data port, which pdn-basic pins to Mic but this one leaves to whatever the radio has.
        f.CcdiModeAllowed.Should().BeFalse();
        f.CcdiProgressMessageEnabled.Should().BeFalse();
        f.TransparentModeEnabled.Should().BeFalse();
        f.DataPort.Should().Be(DataPort.Aux);
    }

    [Fact]
    public void Pdn_basic_profile_enables_the_ccdi_channel_and_wires_the_aux_connector()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);
        f.DataPort = DataPort.Aux;   // so "it is Mic afterwards" is the profile's doing, not the fixture's

        f.ApplyPdnBasic();

        f.CcdiModeAllowed.Should().BeTrue();
        f.PowerupState.Should().Be(DataPowerupMode.CommandMode);
        f.CcdiProgressMessageEnabled.Should().BeTrue();
        f.CommandModeBaud.Should().Be(FfskBaud.Baud28800);
        // the CCDI channel it turns on comes out of the front-panel connector.
        f.DataPort.Should().Be(DataPort.Mic);
        // pdn-basic is telemetry only: it does not turn on the transparent modem.
        f.TransparentModeEnabled.Should().BeFalse();
        // it carries audio-and-ptt: packet audio taps and AUX_GPI1 as the external PTT input.
        AudioAndPttAreWiredForTheAuxConnector(f);
    }

    [Fact]
    public void Pdn_extra_profile_enables_the_transparent_modem_and_includes_basic()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

        f.ApplyPdnExtra();

        // includes pdn-basic
        f.CcdiModeAllowed.Should().BeTrue();
        f.PowerupState.Should().Be(DataPowerupMode.CommandMode);
        f.CcdiProgressMessageEnabled.Should().BeTrue();
        f.DataPort.Should().Be(DataPort.Mic);
        AudioAndPttAreWiredForTheAuxConnector(f);
        // the transparent modem + mode-signalling additions
        f.TransparentModeEnabled.Should().BeTrue();
        f.IgnoreEscapeSequence.Should().BeFalse();          // load-bearing: escape must work
        f.IgnoreSubaudibleOnData.Should().BeTrue();
        f.FfskTransparentBaud.Should().Be(FfskBaud.Baud28800);
        f.FfskModemBaud.Should().Be(FfskModemRate.Baud2400);
        f.SdmEnabled.Should().BeTrue();
        f.CcdiSdmOutputEnabled.Should().BeTrue();
    }

    [Fact]
    public void Pdn_internal_profile_routes_everything_to_the_internal_options_board()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

        f.ApplyPdnInternal();

        // includes pdn-extra (and so pdn-basic)
        f.CcdiModeAllowed.Should().BeTrue();
        f.PowerupState.Should().Be(DataPowerupMode.CommandMode);
        f.CommandModeBaud.Should().Be(FfskBaud.Baud28800);
        f.TransparentModeEnabled.Should().BeTrue();
        // the internal-options additions. The data port wins over the Mic pdn-basic sets.
        f.DataPort.Should().Be(DataPort.InternalOptions);
        f.CommandModeFlowControl.Should().Be(DataFlowControl.None);
        f.GetRxTapOutNode().Should().Be(2);
        f.TapOutUnmute.Should().Be(TapOutUnmute.ExceptOnPtt);
        f.GetEptt1TapInNode().Should().Be(13);
        // the packet-defaults audio block with the tap-out point moved to R2, byte for byte
        Convert.ToHexString(f.Image.Require(0x3B, 0).Data).Should().Be(PacketAudioBlockR2);
        // the keying line lives on the options connector and nowhere else: the AUX_GPI1 input
        // audio-and-ptt (and so pdn-basic) programs is released, so a floating aux pin cannot key it.
        f.GetDigitalIoRole(DigitalIoLine.IopGpio1).Should().Be(DigitalIoRole.ExternalPtt1Input);
        foreach (DigitalIoLine other in Enum.GetValues<DigitalIoLine>().Where(l => l != DigitalIoLine.IopGpio1))
        {
            f.GetDigitalIoRole(other).Should().Be(DigitalIoRole.Unassigned, other.ToString());
        }

        // AUX_GPI1's bits are back to exactly what a default table has, not merely "unassigned".
        byte[] table = f.Image.SectionBytes(0x37);
        byte[] @default = Convert.FromHexString(Fixtures.DefaultDigitalIoTable);
        table[..15].Should().Equal(@default[..15]);   // AUX_GPI1 is entry 0: header, PIN_12, 62 config bits

        // External PTT 1 is still the keying source - only the pin wired to it changed.
        f.GetPttTransmission(PttSource.ExternalPtt1).Should().Be(PttTransmission.DataFromAudioTapIn);
    }

    /// <summary>
    /// The one that matters: applying the wiring to the factory-default Programmable I/O records must
    /// produce the CPS's own bytes for that configuration, in all three records it writes - whether it
    /// is applied on its own or as the part of <c>pdn-basic</c> that carries it. The two codeplugs the
    /// pair comes from differ in exactly these three records and nothing else.
    /// </summary>
    [Fact]
    public void The_aux_connector_wiring_reproduces_the_cps_save_byte_for_byte()
    {
        foreach (Action<CodeplugFields> profile in new Action<CodeplugFields>[] { f => f.ApplyAudioAndPtt(), f => f.ApplyPdnBasic() })
        {
            CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

            profile(f);

            Convert.ToHexString(f.Image.Require(0x19, 0).Data).Should().Be(Fixtures.PacketPttTable);
            Convert.ToHexString(f.Image.Require(0x3B, 0).Data).Should().Be(Fixtures.PacketAudioBlock);
            Convert.ToHexString(f.Image.SectionBytes(0x37)).Should().Be(Fixtures.PacketDigitalIoTable);
        }
    }

    [Fact]
    public void The_default_records_read_back_as_the_cps_shows_them()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

        foreach (PttSource source in Enum.GetValues<PttSource>())
        {
            f.GetPttTransmission(source).Should().Be(PttTransmission.Voice, source.ToString());
        }

        f.GetRxTapOutNode().Should().Be(0);                    // Rx tap out None
        f.TapOutUnmute.Should().Be(TapOutUnmute.OnPtt);
        f.GetDigitalIoRole(DigitalIoLine.AuxGpi1).Should().Be(DigitalIoRole.Unassigned);
    }

    [Fact]
    public void Only_the_named_ptt_source_moves()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

        f.SetPttTransmission(PttSource.ExternalPtt2, PttTransmission.DataFromAudioTapIn);

        f.GetPttTransmission(PttSource.ExternalPtt2).Should().Be(PttTransmission.DataFromAudioTapIn);
        f.GetPttTransmission(PttSource.Ptt).Should().Be(PttTransmission.Voice);
        f.GetPttTransmission(PttSource.ExternalPtt1).Should().Be(PttTransmission.Voice);

        // and back again, byte for byte
        f.SetPttTransmission(PttSource.ExternalPtt2, PttTransmission.Voice);
        Convert.ToHexString(f.Image.Require(0x19, 0).Data).Should().Be(Fixtures.DefaultPttTable);
    }

    [Fact]
    public void An_unrecognised_ptt_combination_is_preserved_and_cannot_be_written()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);
        byte[] table = f.Image.Require(0x19, 0).Data;
        table[5] |= 0x0C;   // bits 11-12 of entry 1 both set: a combination no CPS save has pinned

        f.GetPttTransmission(PttSource.ExternalPtt1).Should().Be(PttTransmission.Other);
        Action act = () => f.SetPttTransmission(PttSource.ExternalPtt1, PttTransmission.Other);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_console_exposes_the_ptt_sources_by_their_form_names()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

        FieldConsole.Get(f, "ptt.eptt1").Should().Be("Voice");
        FieldConsole.Set(f, "ptt.eptt1", "DataFromAudioTapIn");
        f.GetPttTransmission(PttSource.ExternalPtt1).Should().Be(PttTransmission.DataFromAudioTapIn);
        FieldConsole.Get(f, "ptt.ptt").Should().Be("Voice");
        FieldConsole.Get(f, "ptt.eptt2").Should().Be("Voice");
    }

    [Fact]
    public void Pdn_profiles_leave_the_rest_of_the_digital_line_table_alone()
    {
        // The TARPN template has lines this map does not recognise. Applying a profile must not
        // disturb them - only the one line it programs.
        CodeplugFields f = Fixtures.Open(Fixtures.TarpnDigitalIoTable);

        f.ApplyPdnExtra();

        f.GetDigitalIoRole(DigitalIoLine.AuxGpi1).Should().Be(DigitalIoRole.ExternalPtt1Input);
        f.GetDigitalIoRole(DigitalIoLine.IopGpio2).Should().Be(DigitalIoRole.BusyStatusOutput);
        f.GetDigitalIoRole(DigitalIoLine.IopGpio4).Should().Be(DigitalIoRole.Other); // preserved untouched
    }

    /// <summary>
    /// The key edit's counterpart to the wiring test: programming F1 to Squelch Override on the
    /// factory-default Key Settings records must produce the CPS's own bytes, in all three items it
    /// writes - the key table, the function table and the programmed-key list - whether it is set
    /// directly or comes in as the part of a profile that carries it.
    /// </summary>
    [Fact]
    public void The_f1_squelch_override_key_reproduces_the_cps_save_byte_for_byte()
    {
        Action<CodeplugFields>[] ways =
        [
            f => f.SetFunctionKeyRole(FunctionKey.F1, FunctionKeyRole.SquelchOverride),
            f => f.ApplyAudioAndPtt(),
            f => f.ApplyPdnBasic(),
            f => f.ApplyPdnExtra(),
            f => f.ApplyPdnInternal(),
        ];

        foreach (Action<CodeplugFields> apply in ways)
        {
            CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

            apply(f);

            Convert.ToHexString(f.Image.SectionBytes(0x0F)).Should().Be(Fixtures.SquelchOverrideFunctionKeyTable);
            Convert.ToHexString(f.Image.SectionBytes(0x03)).Should().Be(Fixtures.SquelchOverrideFunctionTable);
            Convert.ToHexString(f.Image.SectionBytes(0x18)).Should().Be(Fixtures.SquelchOverrideKeyList);
            f.GetFunctionKeyRole(FunctionKey.F1).Should().Be(FunctionKeyRole.SquelchOverride);
        }
    }

    [Fact]
    public void Every_profile_puts_squelch_override_on_f1_and_leaves_the_other_keys_alone()
    {
        Action<CodeplugFields>[] profiles =
        [
            f => f.ApplyAudioAndPtt(),
            f => f.ApplyPdnBasic(),
            f => f.ApplyPdnExtra(),
            f => f.ApplyPdnInternal(),
        ];

        foreach (Action<CodeplugFields> profile in profiles)
        {
            CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

            profile(f);

            f.GetFunctionKeyRole(FunctionKey.F1).Should().Be(FunctionKeyRole.SquelchOverride);
            foreach (FunctionKey other in Enum.GetValues<FunctionKey>().Where(k => k != FunctionKey.F1))
            {
                f.GetFunctionKeyRole(other).Should().Be(FunctionKeyRole.Unassigned, other.ToString());
            }
        }
    }

    [Fact]
    public void Applying_a_profile_twice_does_not_append_the_key_entry_twice()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

        f.ApplyPdnExtra();
        f.ApplyPdnExtra();

        // the programmed-key list is a set, not a log: re-applying must leave it at one entry.
        Convert.ToHexString(f.Image.SectionBytes(0x18)).Should().Be(Fixtures.SquelchOverrideKeyList);
    }

    [Fact]
    public void Clearing_the_last_key_using_squelch_override_restores_the_default_records()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

        f.SetFunctionKeyRole(FunctionKey.F1, FunctionKeyRole.SquelchOverride);
        f.SetFunctionKeyRole(FunctionKey.F1, FunctionKeyRole.Unassigned);

        // byte for byte back to a default codeplug, including dropping the 0x18 record entirely.
        Convert.ToHexString(f.Image.SectionBytes(0x0F)).Should().Be(Fixtures.DefaultFunctionKeyTable);
        Convert.ToHexString(f.Image.SectionBytes(0x03)).Should().Be(Fixtures.DefaultFunctionTable);
        f.Image.SectionBytes(0x18).Should().BeEmpty();
        f.Image.Find(0x18, 0).Should().BeNull();
    }

    [Fact]
    public void Only_the_named_key_moves()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

        f.SetFunctionKeyRole(FunctionKey.F3, FunctionKeyRole.SquelchOverride);

        f.GetFunctionKeyRole(FunctionKey.F3).Should().Be(FunctionKeyRole.SquelchOverride);
        f.GetFunctionKeyRole(FunctionKey.F1).Should().Be(FunctionKeyRole.Unassigned);
        f.GetFunctionKeyRole(FunctionKey.F2).Should().Be(FunctionKeyRole.Unassigned);
        f.GetFunctionKeyRole(FunctionKey.F4).Should().Be(FunctionKeyRole.Unassigned);

        // and back again, byte for byte
        f.SetFunctionKeyRole(FunctionKey.F3, FunctionKeyRole.Unassigned);
        Convert.ToHexString(f.Image.SectionBytes(0x0F)).Should().Be(Fixtures.DefaultFunctionKeyTable);
    }

    [Fact]
    public void The_function_stays_in_use_while_any_key_still_has_it()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

        f.SetFunctionKeyRole(FunctionKey.F1, FunctionKeyRole.SquelchOverride);
        f.SetFunctionKeyRole(FunctionKey.F2, FunctionKeyRole.SquelchOverride);
        f.SetFunctionKeyRole(FunctionKey.F1, FunctionKeyRole.Unassigned);

        // F2 still uses it, so the function-level records must stay as they are.
        Convert.ToHexString(f.Image.SectionBytes(0x03)).Should().Be(Fixtures.SquelchOverrideFunctionTable);
        Convert.ToHexString(f.Image.SectionBytes(0x18)).Should().Be(Fixtures.SquelchOverrideKeyList);
    }

    [Fact]
    public void The_other_captured_key_functions_read_back_but_cannot_be_written()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

        // the four-key CPS save: F1 Squelch Override, F2 Audible Indicators Volume,
        // F3 Action Digital Output Line, F4 Backlighting Toggle.
        f.Image.SetSectionBytes(0x0F, Convert.FromHexString("00040080006400004001"));

        f.GetFunctionKeyRole(FunctionKey.F1).Should().Be(FunctionKeyRole.SquelchOverride);
        f.GetFunctionKeyRole(FunctionKey.F2).Should().Be(FunctionKeyRole.AudibleIndicatorsVolume);
        f.GetFunctionKeyRole(FunctionKey.F3).Should().Be(FunctionKeyRole.ActionDigitalOutputLine);
        f.GetFunctionKeyRole(FunctionKey.F4).Should().Be(FunctionKeyRole.BacklightingToggle);

        Action act = () => f.SetFunctionKeyRole(FunctionKey.F2, FunctionKeyRole.BacklightingToggle);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_unrecognised_key_function_is_preserved_and_cannot_be_written()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);
        byte[] table = f.Image.Require(0x0F, 0).Data;
        table[0] = 0x7F;   // a 20-bit entry no CPS save has pinned

        f.GetFunctionKeyRole(FunctionKey.F1).Should().Be(FunctionKeyRole.Other);
        Action act = () => f.SetFunctionKeyRole(FunctionKey.F1, FunctionKeyRole.Other);
        act.Should().Throw<ArgumentException>();

        // reading another key must not disturb it
        f.GetFunctionKeyRole(FunctionKey.F2).Should().Be(FunctionKeyRole.Unassigned);
        f.Image.Require(0x0F, 0).Data[0].Should().Be(0x7F);
    }

    [Fact]
    public void The_console_exposes_the_keys_by_their_front_panel_names()
    {
        CodeplugFields f = Fixtures.Open(Fixtures.DefaultDigitalIoTable);

        FieldConsole.Get(f, "key.f1").Should().Be("Unassigned");
        FieldConsole.Set(f, "key.f1", "SquelchOverride");
        f.GetFunctionKeyRole(FunctionKey.F1).Should().Be(FunctionKeyRole.SquelchOverride);
        FieldConsole.Get(f, "key.f1").Should().Be("SquelchOverride");
        FieldConsole.Get(f, "key.f4").Should().Be("Unassigned");
    }

    private static void AudioAndPttAreWiredForTheAuxConnector(CodeplugFields f)
    {
        Convert.ToHexString(f.Image.Require(0x3B, 0).Data).Should().Be(PacketAudioBlock);
        f.GetRxTapOutNode().Should().Be(1);                 // Rx tap out R1
        f.TapOutUnmute.Should().Be(TapOutUnmute.ExceptOnPtt);
        f.GetEptt1TapInNode().Should().Be(13);              // EPTT1 tap in T13
        f.RxTapOutInverted.Should().BeFalse();
        f.Eptt1TapInInverted.Should().BeFalse();
        f.GetDigitalIoRole(DigitalIoLine.AuxGpi1).Should().Be(DigitalIoRole.ExternalPtt1Input);
        f.GetPttTransmission(PttSource.ExternalPtt1).Should().Be(PttTransmission.DataFromAudioTapIn);
    }

}
