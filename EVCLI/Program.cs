/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of EVCLI <https://github.com/OpenChargingCloud/EVCLI>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Globalization;

using Newtonsoft.Json.Linq;

using cloud.charging.open.protocols.ISO15118.SDP.Messages;
using cloud.charging.open.protocols.ISO15118.NetworkInterfaces;

using cloud.charging.open.EV.CommandLine;
using cloud.charging.open.EV.Configuration;
using cloud.charging.open.EV.ISO15118;

using cloud.charging.open.protocols.WWCP.Node.CommandLine;
using cloud.charging.open.protocols.WWCP.Node.Certificates;
using cloud.charging.open.protocols.WWCP.Node.Configuration;

#endregion

namespace cloud.charging.open.EV
{

    /// <summary>
    /// One electric vehicle, with its web interface and a prompt, until
    /// 'quit', Ctrl+C or SIGTERM.
    /// </summary>
    /// <remarks>
    /// What every kind of node's program does is the node's: the switches
    /// every node has and the words -h explains them with, the certificate
    /// store's among them, why it could not be set up or could not start, what
    /// goes into the store at the start, the banner and the prompt. What is
    /// left here is the vehicle's: its own switches and their words, its lines
    /// of the banner, and what it can be asked to do once it is up. And even
    /// that is vocabulary and nothing else - what a vehicle is, and what it
    /// does, lives in the EV library.
    ///
    /// Almost every switch of its own is a setting that is written to the
    /// configuration file, so it is said once rather than at every start - and
    /// so that the web interface and the command line never disagree about
    /// what this vehicle is. The exceptions are the switches that make
    /// something happen.
    /// </remarks>
    public class Program
    {

        #region (private static) Usage

        /// <summary>
        /// What -h shows: every node's switches, in a vehicle's words, and the
        /// vehicle's own before the log's.
        /// </summary>
        private static readonly NodeUsage Usage = new (

            Program:            "EVCLI",
            Kind:               EV.EVKind,
            DefaultPort:        EV.DefaultHTTPPort,
            FrontendSources:    "libs/EV/EV/Frontend",

            ConfigurationSays:  "where everything this vehicle is told in writing lives (default: " +
                               $"{WWCPConfigFile.DefaultFileName} below the repository root). The settings of the vehicle, " +
                                "of finding a station and of the session below are written to it, so each is said once " +
                                "rather than at every start; the Configuration pages edit the same file.",

            Synopsis:           [
                                    "[--name <name>]", "[--vin <vin>]",
                                    "[--battery <kWh>]", "[--soc <percent>]", "[--target-soc <percent>]",
                                    "[--power <kW>]", "[--taper-from <percent>]",
                                    "[--interface <name>]", "[--no-tls]", "[--sdp]",
                                    "[--connect <host:port>]", "[--protocol 2|20|both]", "[--mode ac|dc|mcs]",
                                    "[--tls | --tls-backend dotnet|bc]", "[--pki-dir <dir>]",
                                    "[--vehicle-cert <handle>]", "[--contract-cert <handle>]",
                                    "[--oem-cert <handle>]", "[--tariff-cert <handle>]",
                                    "[--target-energy <kWh>]", "[--max-charging-time <dur>]",
                                    "[--departure-time <dur>]", "[--min-soc <percent>]", "[--renegotiate]",
                                    "[--slac-peer <host:port>]", "[--slac]",
                                    "[--t1s-transport none|auto|afpacket|udp]", "[--t1s-bus <group:port>]",
                                    "[--t1s-interface <name>]", "[--t1s-weight <1..8>]", "[--t1s]", "[--charge]",
                                    "[--pause | --pause-resume | --resume <hex>]"
                                ],

            BeforeTheLog:       [

                                    "This vehicle:",
                                    .. NodeUsage.Switch("--name <name>",               $"what to call it (default: {VehicleConfiguration.DefaultName})"),
                                    .. NodeUsage.Switch("--vin <vin>",                  "its vehicle identification number"),
                                    .. NodeUsage.Switch("--battery <kWh>",             $"the usable capacity of the pack (default: {VehicleConfiguration.DefaultCapacity_kWh:F0})"),
                                    .. NodeUsage.Switch("--soc <percent>",             $"how full it is at plug-in (default: {VehicleConfiguration.DefaultSoC_percent:F0})"),
                                    .. NodeUsage.Switch("--target-soc <percent>",       "charge until this state of charge"),
                                    .. NodeUsage.Switch("--power <kW>",                 "what it asks a station for"),
                                    .. NodeUsage.Switch("--taper-from <percent>",      $"where it starts asking for less; 100 charges flat (default: {VehicleConfiguration.DefaultTaperFrom_percent:F0})"),
                                    "",

                                    "Finding a station (SDP):",
                                    .. NodeUsage.Switch("--interface <name>",           "the interface the station is on, i.e. the powerline modem; without one the first candidate " +
                                                                                        "with an IPv6 link-local address is taken, and the console says which"),
                                    .. NodeUsage.Switch("--no-tls",                     "ask stations for a plain TCP endpoint rather than a TLS one, and accept the plain one they " +
                                                                                        "then offer - both, because asking for one and refusing it is only a slower timeout"),
                                    "",

                                    "The session:",
                                    .. NodeUsage.Switch("--connect <host:port>",        "the station to drive to, instead of looking for one. IPv6 literals must be bracketed: " +
                                                                                        "[fe80::1%eth0]:15118"),
                                    .. NodeUsage.Switch("--protocol 2|20|both",         "what to offer (default: both, -20 at priority 1). \"both\" offers each in one handshake and " +
                                                                                        "runs whichever the station picks, which is what a modern vehicle does"),
                                    .. NodeUsage.Switch("--mode ac|dc|mcs",             "the energy transfer mode (default: dc). Not negotiated - the station has to have been told " +
                                                                                        "the same. \"mcs\" is the DC message set under energy-transfer services 8/9; -20 only"),
                                    .. NodeUsage.Switch("--renegotiate",                "-2: send PowerDelivery(Renegotiate) after the first cycle"),
                                    "",
                                    "  Charging goals. Name none and the goal is --target-soc, or full:",
                                    .. NodeUsage.Switch("--target-energy <kWh>",        "charge until this much has been delivered"),
                                    .. NodeUsage.Switch("--max-charging-time <dur>",    "stop after this much simulated time: 90, 90m, 2h, 1h30m. One iteration is one simulated " +
                                                                                        "minute, so a full charge is hundreds of exchanges - give this when driving a live station"),
                                    .. NodeUsage.Switch("--departure-time <dur>",       "when the vehicle leaves. On the wire in -20, and the end of the session in both"),
                                    .. NodeUsage.Switch("--min-soc <percent>",          "what the driver needs by then. A floor, not a goal: it cannot prolong a session, and the " +
                                                                                        "run says whether it was met"),
                                    "",

                                    "TLS:",
                                    .. NodeUsage.Switch("--tls",                        ".NET SslStream. Fast and native, and on Windows and macOS unable to carry the -20 profile " +
                                                                                        "at all - see the EVCC README"),
                                    .. NodeUsage.Switch("--tls-backend dotnet|bc",      "bc = BouncyCastle, the -20-faithful profile (TLS 1.3, secp521r1, mutual). Required on " +
                                                                                        "Windows and macOS for a real -20 session. Wins over --tls when both are given"),
                                    .. NodeUsage.Switch("--pki-dir <dir>",              "the development hierarchy a station minted: this vehicle reads its own chain out of it " +
                                                                                        "and pins the station's leaf"),
                                    "",

                                    .. NodeUsage.Wrap("Which of the store's certificates one session uses. Each names a handle --list-certificates " +
                                                      "prints, and they are not interchangeable. A root needs none: it is believed as soon as it " +
                                                      "is in, every usable one of its kind. A credential has one slot, so importing one also " +
                                                      "chooses it - name a handle here to choose a different one:", "", ""),
                                    .. NodeUsage.Switch("--vehicle-cert <handle>",      "the Vehicle certificate - who this vehicle is. Presented in the TLS handshake; for -20 " +
                                                                                        "the station's resume binding is computed over it"),
                                    .. NodeUsage.Switch("--contract-cert <handle>",     "the contract certificate - who pays. Plug & Charge"),
                                    .. NodeUsage.Switch("--oem-cert <handle>",          "the OEM provisioning certificate - what the vehicle was born with. -20 asks the station " +
                                                                                        "to issue a contract with it and unwraps the key it sends back"),
                                    .. NodeUsage.Switch("--tariff-cert <handle>",       "the public key a station's signed tariff is checked with"),
                                    "",

                                    "SLAC, the pairing stage before SDP:",
                                    .. NodeUsage.Switch("--slac-peer <host:port>",      "the station's SLAC endpoint on a simulated medium. Real SLAC is EtherType 0x88E1 over " +
                                                                                        "AF_PACKET and needs Linux and CAP_NET_RAW; this runs the same state machine over UDP"),
                                    "",

                                    "MCS: the 10BASE-T1S bus below a megawatt coupler, joined before SDP:",
                                    .. NodeUsage.Switch("--t1s-transport <kind>",       "none, auto, afpacket or udp. auto takes a real adapter (AF_PACKET; Linux and " +
                                                                                        "CAP_NET_RAW) where there is one and nothing anywhere else; udp is the emulated medium " +
                                                                                        "and has to be asked for"),
                                    .. NodeUsage.Switch("--t1s-bus <group:port>",       "the multicast group that is the emulated bus, default 239.151.18.1:16118. Naming one " +
                                                                                        "implies --t1s-transport udp"),
                                    .. NodeUsage.Switch("--t1s-interface <name>",       "the adapter, for afpacket (default: the V2G interface); the interface to join the group " +
                                                                                        "on, for udp (default: the OS picks)"),
                                    .. NodeUsage.Switch("--t1s-weight <1..8>",          "transmit opportunities per cycle to ask the station for, default 3 - more than any " +
                                                                                        "sensor in the coupler gets"),
                                    "",

                                    "Doing something at a start. The vehicle's switches above only configure:",
                                    .. NodeUsage.Switch("--slac",                       "pair once, and print what came of it"),
                                    .. NodeUsage.Switch("--t1s",                        "join the coupler's bus once, stay on it two seconds, and print what came of it"),
                                    .. NodeUsage.Switch("--sdp",                        "look for a station once, and print what answered"),
                                    .. NodeUsage.Switch("--charge",                     "run one session once the vehicle is up, and print the result. The whole exchange is in " +
                                                                                        "the log and on the event stream while it happens. The web interface does the same from " +
                                                                                        "a button"),
                                    .. NodeUsage.Switch("--pause",                      "end that session paused rather than terminated, and print its identification so that " +
                                                                                        "another run can rejoin it"),
                                    .. NodeUsage.Switch("--resume <hex>",               "rejoin a paused session by that identification"),
                                    .. NodeUsage.Switch("--pause-resume",               "both halves in one run: charge, pause, reconnect, rejoin"),
                                    ""

                                ]

        );

        #endregion

        #region (private static) TryTakeNumber(Arguments, ref Index, Flag, out Value)

        /// <summary>
        /// A number after a switch, or a sentence about why there is none.
        /// </summary>
        private static Boolean TryTakeNumber(IReadOnlyList<String>  Arguments,
                                             ref Int32              Index,
                                             String                 Flag,
                                             out Double             Value)
        {

            if (NodeArguments.TryTakeValue(Arguments, ref Index, out var text) &&
                Double.TryParse(text.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out Value))
            {
                return true;
            }

            NodeProgram.Say(Console.Error, $"Missing or invalid number after {Flag}!");
            Value = 0;
            return false;

        }

        #endregion

        #region (private static) TryTakeDuration(Arguments, ref Index, Flag, out Value)

        /// <summary>
        /// <c>90</c>, <c>90m</c>, <c>2h</c>, <c>45s</c> or <c>1h30m</c>.
        /// </summary>
        /// <remarks>
        /// A bare number is minutes, because that is the unit a charging
        /// session is discussed in and the one a charge-loop iteration stands
        /// for. The same grammar the EVCC of WWCP_ISO15118 accepts, so that a
        /// run described for one can be typed at the other.
        /// </remarks>
        private static Boolean TryTakeDuration(IReadOnlyList<String>  Arguments,
                                               ref Int32              Index,
                                               String                 Flag,
                                               out TimeSpan           Value)
        {

            Value = TimeSpan.Zero;

            if (!NodeArguments.TryTakeValue(Arguments, ref Index, out var raw))
            {
                NodeProgram.Say(Console.Error, $"Missing duration after {Flag}! Try 90, 90m, 2h or 1h30m.");
                return false;
            }

            var text  = raw.Trim().ToLowerInvariant();
            var total = TimeSpan.Zero;
            var seen  = false;

            for (var i = 0; i < text.Length;)
            {

                var start = i;

                while (i < text.Length && (Char.IsDigit(text[i]) || text[i] == '.'))
                    i++;

                if (i == start ||
                    !Double.TryParse(text[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    NodeProgram.Say(Console.Error, $"{Flag}: '{raw}' is not a duration. Try 90, 90m, 2h or 1h30m.");
                    return false;
                }

                var unit = i < text.Length ? text[i++] : 'm';

                switch (unit)
                {
                    case 's':  total += TimeSpan.FromSeconds(number);  break;
                    case 'm':  total += TimeSpan.FromMinutes(number);  break;
                    case 'h':  total += TimeSpan.FromHours  (number);  break;
                    default:
                        NodeProgram.Say(Console.Error, $"{Flag}: unknown unit '{unit}' in '{raw}' - use s, m or h.");
                        return false;
                }

                seen = true;

            }

            if (!seen || total <= TimeSpan.Zero)
            {
                NodeProgram.Say(Console.Error, $"{Flag} expects a duration greater than zero, got '{raw}'.");
                return false;
            }

            Value = total;
            return true;

        }

        #endregion

        #region (private static) DescribeV2GInterface(Configured, Candidates)

        /// <summary>
        /// Which interface a discovery would use, and - when nobody said -
        /// what made it that one, for the banner: in lines of 80 columns with
        /// its column, broken between words and never in the name of an
        /// interface, however many words that has.
        /// </summary>
        /// <remarks>
        /// A guess that does not announce itself is how somebody spends an
        /// afternoon wondering which cable is in use, so the reason is part of
        /// the answer rather than something to go and read in the source.
        ///
        /// Said in one line, a machine with three virtual switches, each named
        /// like "vEthernet (Neuer virtueller Switch)", made one of 203 columns,
        /// where the node's banner keeps even its time servers to 80.
        /// </remarks>
        private static String DescribeV2GInterface(String?                            Configured,
                                                   IReadOnlyList<V2GNetworkInterface>  Candidates)
        {

            // The banner puts every line of a value at its column, which none
            // of the vehicle's labels moves; WrapItems keeps a line to 80 with
            // what it begins with, so each begins with the column here.
            var column = new String(' ', NodeBanner.Column);

            return String.Join("\n", NodeUsage.WrapItems(WordsOfTheV2GInterface(Configured, Candidates), column, column).
                                               Select(line => line[column.Length..]));

        }

        #endregion

        #region (private static) WordsOfTheV2GInterface(Configured, Candidates)

        /// <summary>
        /// What <see cref="DescribeV2GInterface"/> says, word by word: the name
        /// of an interface one word, a comma or a dash after it with it.
        /// </summary>
        private static IEnumerable<String> WordsOfTheV2GInterface(String?                            Configured,
                                                                  IReadOnlyList<V2GNetworkInterface>  Candidates)
        {

            if (Configured is not null)
                return [ Configured ];

            if (Candidates.Count == 0)
                return Words("none - this machine has no interface that could carry V2G traffic");

            var chosen = V2GLink.Choose(Candidates);

            if (chosen is null)
                return [ "none" ];

            if (Candidates.Count == 1)
                return [ chosen.Name ];

            var withoutIPv4 = Candidates.Where(candidate => !candidate.HasIPv4Address).ToArray();

            return withoutIPv4.Length switch {

                       1   => [ chosen.Name, .. Words("(the only one of"), .. Listed(Candidates,  ""),   .. Words("without an IPv4 address)") ],

                       // Narrowed but not decided: say both, so that nobody reads
                       // a coin toss as a conclusion. --interface settles it.
                       > 1 => [ chosen.Name, .. Words("(first of"),        .. Listed(withoutIPv4, ","),  .. Words("which have no IPv4 address)") ],

                       _   => [ chosen.Name, .. Words("(first of"),        .. Listed(Candidates,  " -"), .. Words("every one of them has an IPv4 address)") ]

                   };

        }

        #endregion

        #region (private static) Words(Text) / Listed(Interfaces, AfterTheLast)

        /// <summary>
        /// The words of a text, a dash with the word before it, as the node
        /// keeps it in what it breaks.
        /// </summary>
        private static List<String> Words(String Text)
        {

            var words = new List<String>();

            foreach (var word in Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (word == "-" && words.Count > 0)
                    words[^1] += " -";
                else
                    words.Add(word);
            }

            return words;

        }

        /// <summary>
        /// The names of interfaces, one word each, a comma after every one but
        /// the last, and what the given text says after the last.
        /// </summary>
        private static IEnumerable<String> Listed(IReadOnlyList<V2GNetworkInterface>  Interfaces,
                                                  String                              AfterTheLast)

            => Interfaces.Select((candidate, index) => candidate.Name + (index < Interfaces.Count - 1 ? "," : AfterTheLast));

        #endregion


        public static async Task<Int32> Main(String[] Arguments)
        {

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            #region Arguments

            // Every node's switches, the certificate store's among them; what
            // is left is the vehicle's, as it was typed.
            var arguments = NodeArguments.Parse(Arguments);

            if (arguments.Refused(Usage) is Int32 refused)
                return refused;

            String?  name           = null;
            String?  vin            = null;
            Double?  battery        = null;
            Double?  soc            = null;
            Double?  targetSoC      = null;
            Double?  power          = null;
            Double?  taperFrom      = null;

            String?  interfaceName  = null;
            var      noTLS          = false;

            String?    connect       = null;
            String?    protocolText  = null;
            String?    modeText      = null;
            var        tls           = false;
            String?    tlsBackend    = null;
            String?    pkiDir        = null;
            String?    vehicleCert   = null;
            String?    contractCert  = null;
            String?    oemCert       = null;
            String?    tariffCert    = null;
            String?    slacPeer      = null;
            String?    t1sTransport  = null;
            String?    t1sBus        = null;
            String?    t1sInterface  = null;
            Byte?      t1sWeight     = null;
            Boolean?   renegotiate   = null;
            Double?    targetEnergy  = null;
            Double?    minimumSoC    = null;
            TimeSpan?  maxTime       = null;
            TimeSpan?  departure     = null;

            var      pairAtStart     = false;
            var      attachAtStart   = false;
            var      discoverAtStart = false;
            var      chargeAtStart   = false;
            var      pause           = false;
            var      pauseResume     = false;
            String?  resumeFrom      = null;

            var rest = arguments.Rest;

            for (var i = 0; i < rest.Count; i++)
            {
                switch (rest[i])
                {

                    #region What this vehicle is

                    case "--name":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out name))
                        {
                            NodeProgram.Say(Console.Error, "Missing name after --name!");
                            return 2;
                        }
                        break;

                    case "--vin":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out vin))
                        {
                            NodeProgram.Say(Console.Error, "Missing identification after --vin!");
                            return 2;
                        }
                        break;

                    case "--battery":
                        if (!TryTakeNumber(rest, ref i, "--battery", out var parsedBattery))
                            return 2;
                        battery = parsedBattery;
                        break;

                    case "--soc":
                        if (!TryTakeNumber(rest, ref i, "--soc", out var parsedSoC))
                            return 2;
                        soc = parsedSoC;
                        break;

                    case "--target-soc":
                        if (!TryTakeNumber(rest, ref i, "--target-soc", out var parsedTargetSoC))
                            return 2;
                        targetSoC = parsedTargetSoC;
                        break;

                    case "--power":
                        if (!TryTakeNumber(rest, ref i, "--power", out var parsedPower))
                            return 2;
                        power = parsedPower;
                        break;

                    case "--taper-from":
                        if (!TryTakeNumber(rest, ref i, "--taper-from", out var parsedTaper))
                            return 2;
                        taperFrom = parsedTaper;
                        break;

                    #endregion

                    #region Finding a station

                    case "--interface":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out interfaceName))
                        {
                            NodeProgram.Say(Console.Error, "Missing interface name after --interface!");
                            return 2;
                        }
                        break;

                    case "--no-tls":
                        noTLS = true;
                        break;

                    #endregion

                    #region The session

                    case "--connect":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out connect))
                        {
                            NodeProgram.Say(Console.Error, "Missing host:port after --connect!");
                            return 2;
                        }
                        break;

                    case "--protocol":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out protocolText))
                        {
                            NodeProgram.Say(Console.Error, "Missing 2, 20 or both after --protocol!");
                            return 2;
                        }
                        break;

                    case "--mode":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out modeText))
                        {
                            NodeProgram.Say(Console.Error, "Missing ac, dc or mcs after --mode!");
                            return 2;
                        }
                        break;

                    case "--renegotiate":
                        renegotiate = true;
                        break;

                    case "--target-energy":
                        if (!TryTakeNumber(rest, ref i, "--target-energy", out var parsedEnergy))
                            return 2;
                        targetEnergy = parsedEnergy;
                        break;

                    case "--min-soc":
                        if (!TryTakeNumber(rest, ref i, "--min-soc", out var parsedMinimum))
                            return 2;
                        minimumSoC = parsedMinimum;
                        break;

                    case "--max-charging-time":
                        if (!TryTakeDuration(rest, ref i, "--max-charging-time", out var parsedMaxTime))
                            return 2;
                        maxTime = parsedMaxTime;
                        break;

                    case "--departure-time":
                        if (!TryTakeDuration(rest, ref i, "--departure-time", out var parsedDeparture))
                            return 2;
                        departure = parsedDeparture;
                        break;

                    #endregion

                    #region TLS, and which certificates a session uses

                    case "--tls":
                        tls = true;
                        break;

                    case "--tls-backend":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out tlsBackend))
                        {
                            NodeProgram.Say(Console.Error, "Missing dotnet or bc after --tls-backend!");
                            return 2;
                        }
                        break;

                    case "--pki-dir":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out pkiDir))
                        {
                            NodeProgram.Say(Console.Error, "Missing directory after --pki-dir!");
                            return 2;
                        }
                        break;

                    case "--vehicle-cert":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out vehicleCert))
                        {
                            NodeProgram.Say(Console.Error, "Missing handle after --vehicle-cert!");
                            return 2;
                        }
                        break;

                    case "--contract-cert":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out contractCert))
                        {
                            NodeProgram.Say(Console.Error, "Missing handle after --contract-cert!");
                            return 2;
                        }
                        break;

                    case "--oem-cert":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out oemCert))
                        {
                            NodeProgram.Say(Console.Error, "Missing handle after --oem-cert!");
                            return 2;
                        }
                        break;

                    case "--tariff-cert":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out tariffCert))
                        {
                            NodeProgram.Say(Console.Error, "Missing handle after --tariff-cert!");
                            return 2;
                        }
                        break;

                    #endregion

                    #region SLAC

                    case "--slac-peer":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out slacPeer))
                        {
                            NodeProgram.Say(Console.Error, "Missing host:port after --slac-peer!");
                            return 2;
                        }
                        break;

                    #endregion

                    #region T1S

                    case "--t1s-transport":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out t1sTransport))
                        {
                            NodeProgram.Say(Console.Error, "Missing kind after --t1s-transport! One of none, auto, afpacket, udp.");
                            return 2;
                        }
                        break;

                    case "--t1s-bus":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out t1sBus))
                        {
                            NodeProgram.Say(Console.Error, "Missing group:port after --t1s-bus!");
                            return 2;
                        }
                        break;

                    case "--t1s-interface":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out t1sInterface))
                        {
                            NodeProgram.Say(Console.Error, "Missing interface name after --t1s-interface!");
                            return 2;
                        }
                        break;

                    case "--t1s-weight":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out var weightText) ||
                            !Byte.TryParse(weightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight) ||
                            weight < 1 || weight > 8)
                        {
                            NodeProgram.Say(Console.Error, "Missing or invalid weight after --t1s-weight! Between 1 and 8 transmit opportunities per cycle.");
                            return 2;
                        }
                        t1sWeight = weight;
                        break;

                    #endregion

                    #region What to actually do at a start

                    case "--slac":
                        pairAtStart = true;
                        break;

                    case "--t1s":
                        attachAtStart = true;
                        break;

                    case "--sdp":
                        discoverAtStart = true;
                        break;

                    case "--charge":
                        chargeAtStart = true;
                        break;

                    case "--pause":
                        pause          = true;
                        chargeAtStart  = true;
                        break;

                    case "--pause-resume":
                        pauseResume    = true;
                        chargeAtStart  = true;
                        break;

                    case "--resume":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out resumeFrom))
                        {
                            NodeProgram.Say(Console.Error, "Missing session identification after --resume!");
                            return 2;
                        }
                        chargeAtStart = true;
                        break;

                    #endregion

                    default:
                        return NodeArguments.Unknown(rest[i], Usage);

                }
            }

            if (pause && pauseResume)
            {
                NodeProgram.Say(Console.Error, "--pause ends the session paused and stops there; --pause-resume goes on to rejoin it. " +
                                               "They ask for different runs.");
                return 2;
            }

            var root = NodeProgram.RepositoryRoot("EVCLI.slnx");

            #endregion

            #region The vehicle

            EV vehicle;

            try
            {
                vehicle = new EV(
                              HTTPHostname:      arguments.HTTPHostname,
                              HTTPPort:          arguments.Port,
                              AccountsPath:      arguments.AccountsPathBelow(root),
                              ConfigFile:        new WWCPConfigFile(arguments.ConfigFilePathBelow(root)),
                              Frontend:          arguments.Frontend,
                              CertificatesPath:  arguments.CertificatesPath,
                              ConsoleLogLevel:   arguments.ConsoleLogLevel,
                              LogPath:           arguments.LogPathBelow(root),
                              BridgeDebugLog:    !arguments.NoTrace,
                              SSH:               arguments.SSH
                          );
            }
            catch (Exception e)
            {
                return NodeProgram.CouldNotBeSetUp(EV.EVKind, e, arguments.Verbose);
            }

            #endregion

            await using (vehicle)
            {

                // What somebody signed in over SSH gets: the vehicle's own
                // command line, with discover beside the node's commands.
                vehicle.CommandLines = (terminal, caller) => new VehicleCLI(vehicle, terminal, caller);

                #region What the switches said about this vehicle

                // Written to the configuration file rather than held in this
                // process, and for the same reason the web interface writes it:
                // a figure given on a command line and kept nowhere is a figure
                // that has to be typed again at every start, and that quietly
                // differs from what the web interface shows.
                if (name is not null || vin is not null || battery.HasValue || soc.HasValue ||
                    targetSoC.HasValue || power.HasValue || taperFrom.HasValue)
                {

                    var told = new VehicleConfiguration(
                                   name,
                                   vin,
                                   battery,
                                   soc,
                                   power,
                                   taperFrom,
                                   targetSoC
                               ).ToJSON();

                    if (!vehicle.TryUpdateVehicleConfiguration(told, out var problem))
                    {
                        NodeProgram.Say(Console.Error, $"The vehicle could not be configured: {problem}");
                        return 2;
                    }

                }

                if (interfaceName is not null || noTLS)
                {

                    // --no-tls moves the acceptance policy with the request, or
                    // it could never succeed: the client refuses a no-TLS
                    // answer whenever RejectNoTLSResponses is on, without
                    // looking at what was asked for. A vehicle that asked for
                    // plain TCP and then threw away the plain TCP it was
                    // offered would be a switch that does nothing but time out.
                    //
                    // The station has always done this on its side - its
                    // BuildSdpOptions sets RejectNoTlsRequests = !noTls for the
                    // same reason - so this is the missing half of a pair
                    // rather than a new idea.
                    //
                    // Only on the way down. Nothing here turns the rejection
                    // back on, because a vehicle that has been told to accept
                    // TLS again is a vehicle whose configuration says so.
                    var told = new V2GConfiguration(
                                   InterfaceName:         interfaceName,
                                   RequestedSecurity:     noTLS ? SDP_Security.NoTLS : null,
                                   RejectNoTLSResponses:  noTLS ? false            : null
                               ).ToJSON();

                    if (!vehicle.TryUpdateV2GConfiguration(told, out var problem))
                    {
                        NodeProgram.Say(Console.Error, $"The link could not be configured: {problem}");
                        return 2;
                    }

                }

                #endregion

                #region What the switches said about certificates

                // Before the session's switches below, so that a certificate
                // imported in this run can be what they name.
                if (vehicle.ImportCertificates(arguments, out var imported) is Int32 notImported)
                    return notImported;

                // A credential has exactly one slot and the kind already named
                // it, so importing one is choosing it. A root has no slot:
                // every usable one of its kind is believed, and there is
                // nothing to choose.
                foreach (var entry in imported)
                {
                    if      (entry.Kind == CertificateKind.Vehicle)             vehicleCert  ??= entry.Id;
                    else if (entry.Kind == CertificateKind.Contract)            contractCert ??= entry.Id;
                    else if (entry.Kind == CertificateKind.OEMProvisioning)     oemCert      ??= entry.Id;
                    else if (entry.Kind == CertificateKind.TariffVerification)  tariffCert   ??= entry.Id;
                }

                #endregion

                #region What the switches said about a session

                String? sessionRefused = null;

                if (connect is not null || protocolText is not null || modeText is not null || tls ||
                    tlsBackend is not null || pkiDir is not null ||
                    vehicleCert is not null || contractCert is not null || oemCert is not null ||
                    tariffCert is not null || slacPeer is not null || renegotiate.HasValue ||
                    t1sTransport is not null || t1sBus is not null || t1sInterface is not null || t1sWeight.HasValue ||
                    targetEnergy.HasValue || minimumSoC.HasValue || maxTime.HasValue || departure.HasValue)
                {

                    // The section's own vocabulary, so that what a switch means
                    // and what the file means are the same thing parsed by the
                    // same parser - including every refusal, which then names
                    // the field rather than the switch.
                    var told = new JObject();

                    if (connect      is not null)  told["connect"]                      = connect;
                    if (protocolText is not null)  told["protocol"]                     = protocolText;
                    if (modeText     is not null)  told["mode"]                         = modeText;
                    if (pkiDir       is not null)  told["pkiDirectory"]                 = pkiDir;
                    if (vehicleCert  is not null)  told["vehicleCertificate"]           = vehicleCert;
                    if (contractCert is not null)  told["contractCertificate"]          = contractCert;
                    if (oemCert      is not null)  told["oemCertificate"]               = oemCert;
                    if (tariffCert   is not null)  told["tariffCertificate"]            = tariffCert;
                    if (slacPeer     is not null)  told["slacPeer"]                     = slacPeer;
                    if (t1sTransport is not null)  told["t1sTransport"]                 = t1sTransport;
                    if (t1sBus       is not null)  told["t1sBus"]                       = t1sBus;
                    if (t1sInterface is not null)  told["t1sInterface"]                 = t1sInterface;
                    if (t1sWeight.HasValue)        told["t1sWeight"]                    = t1sWeight.Value;
                    if (renegotiate.HasValue)      told["renegotiate"]                  = renegotiate.Value;
                    if (targetEnergy.HasValue)     told["targetEnergyKWh"]              = targetEnergy.Value;
                    if (minimumSoC.HasValue)       told["minimumStateOfChargePercent"]  = minimumSoC.Value;
                    if (maxTime.HasValue)          told["maxChargingTimeSeconds"]       = maxTime.  Value.TotalSeconds;
                    if (departure.HasValue)        told["departureInSeconds"]           = departure.Value.TotalSeconds;

                    // --tls is shorthand for the .NET backend, and
                    // --tls-backend wins where both were given.
                    if (tlsBackend is not null)
                        told["tls"] = tlsBackend;
                    else if (tls)
                        told["tls"] = "dotnet";

                    if (!vehicle.TryUpdateSessionConfiguration(told, out var problem))
                        sessionRefused = problem;

                }

                #endregion

                #region The certificate store, where --list-certificates asked for it

                // After the session's switches, so that what the list marks as
                // chosen is what this run chose - importing a credential
                // chooses it, and so does a handle given here - and before a
                // refusal of them, so that a handle somebody mistyped is
                // answered with the handles there are.
                if (arguments.ListCertificates)
                    vehicle.ListCertificates(Beside: entry => vehicle.UsedBySession(entry.Id) is not null
                                                                  ? "<- chosen"
                                                                  : null);

                if (sessionRefused is not null)
                {
                    NodeProgram.Say(Console.Error, $"The session could not be configured: {sessionRefused}");
                    return 2;
                }

                #endregion

                if (await vehicle.Started(arguments.Verbose) is Int32 notStarted)
                    return notStarted;

                #region What somebody who just started this needs to know

                var session = vehicle.SessionSettings;

                foreach (var line in vehicle.Banner(
                                         OfTheKind: [
                                             ("vehicle",        $"{vehicle.VehicleName}{(vehicle.VIN is not null ? $" ({vehicle.VIN})" : "")}"),
                                             ("battery",        String.Create(CultureInfo.InvariantCulture,
                                                                              $"{vehicle.StateOfCharge_percent:F0} % of {vehicle.BatteryCapacity_kWh:F0} kWh, " +
                                                                              $"asking for {vehicle.MaxChargingPower_kW:F1} kW up to {vehicle.TargetStateOfCharge_percent:F0} %"))
                                         ],
                                         AfterTheTimeServers: [
                                             ("V2G interface",  DescribeV2GInterface(vehicle.V2GSettings.InterfaceName, V2GLink.Candidates())),
                                             ("station",        session.Connect ?? "whichever one answers an SDP request"),
                                             ("session",        $"{session.ProtocolWritten ?? "both"}, " +
                                                                $"{(session.ModeWritten ?? "dc").ToUpperInvariant()}, " +
                                                                $"TLS {session.TLSWritten ?? "none"}" +
                                                                (session.SLACPeer is not null ? $", SLAC over {session.SLACPeer}" : ""))
                                         ]))
                    Console.WriteLine(line);

                #endregion

                #region Whatever the command line asked this vehicle to actually do

                // After the web interface is up, so that a browser opened on the
                // Logs page while these run sees the exchange happen rather than
                // finding it already over.

                if (pairAtStart)
                {
                    var paired = await vehicle.PairAsync();
                    Console.WriteLine($"  SLAC           {PairOutcome(paired)}");
                    Console.WriteLine();
                }

                if (attachAtStart)
                {
                    var attached = await vehicle.AttachToBusAsync();
                    Console.WriteLine($"  T1S            {AttachOutcome(attached)}");
                    Console.WriteLine();
                }

                if (discoverAtStart)
                {
                    var found = await vehicle.DiscoverAsync();
                    Console.WriteLine($"  SDP            {DiscoveryOutcome(found)}");
                    Console.WriteLine();
                }

                if (chargeAtStart)
                {

                    var charged = await vehicle.RunSessionAsync(
                                      Pause:        pause,
                                      ResumeFrom:   resumeFrom,
                                      PauseResume:  pauseResume
                                  );

                    Console.WriteLine($"  session        {SessionOutcome(charged)}");

                    if (charged["battery"] is JObject pack && BatteryOutcome(pack) is { } packOutcome)
                        Console.WriteLine($"  battery        {packOutcome}");

                    if (charged.Value<String>("pausedSessionId") is { } pausedId)
                        Console.WriteLine($"  paused as      {pausedId}  (rejoin it with --resume {pausedId})");

                    Console.WriteLine();

                }

                #endregion

                #region The command line, until 'quit', Ctrl+C or SIGTERM

                // The node's: a prompt where somebody can type, and waiting
                // where nobody can, with the log sharing the screen.
                await new VehicleCLI(vehicle).RunUntilStopped();

                #endregion

            }

            return 0;

        }


        #region (private static) DiscoveryOutcome(Discovery) / PairOutcome(Pairing) / SessionOutcome(Session) / BatteryOutcome(Battery)

        /// <summary>
        /// How a discovery went, in one line for the console. The whole of it
        /// is in the log either way.
        /// </summary>
        internal static String DiscoveryOutcome(JObject Discovery)
        {

            var outcome = Discovery.Value<String>("outcome");

            if (outcome == "found" && Discovery["secc"] is JObject secc)
                return $"a station at [{secc.Value<String>("address")}]:{secc.Value<Int32>("port")} " +
                       $"({(secc.Value<String>("security") == "tls" ? "TLS" : "no TLS")}), " +
                       $"after {Discovery.Value<Int32>("attempts")} request(s) in {Discovery.Value<Double>("elapsed_ms"):F0} ms";

            return outcome switch {
                       "rejected"     => "something answered, and none of the answers was usable - see the log",
                       "timeout"      => $"nothing answered after {Discovery.Value<Int32>("attempts")} request(s)",
                       "noInterface"  => Discovery.Value<String>("error") ?? "there was nothing to broadcast on",
                       "cancelled"    => "cancelled",
                       _              => Discovery.Value<String>("error") ?? "the discovery failed - see the log"
                   };

        }

        /// <summary>
        /// How a SLAC pairing went, in one line.
        /// </summary>
        private static String PairOutcome(JObject Pairing)

            => Pairing.Value<String>("outcome") switch {
                   "paired"          => $"paired with {Pairing.Value<String>("peer")} in {Pairing.Value<Double>("elapsed_ms"):F0} ms, " +
                                        $"network {Pairing.Value<String>("nid")}",
                   "notConfigured"   => "no peer configured - give --slac-peer <host:port>",
                   "cancelled"       => "cancelled",
                   _                 => Pairing.Value<String>("error") ?? "the pairing failed - see the log"
               };

        /// <summary>
        /// How joining the coupler's bus went, in one line.
        /// </summary>
        private static String AttachOutcome(JObject Attachment)

            => Attachment.Value<String>("outcome") switch {
                   "attached"        => $"on the bus as node {Attachment.Value<Int32>("nodeId")} over {Attachment.Value<String>("medium")} " +
                                        $"in {Attachment.Value<Double>("elapsed_ms"):F0} ms, " +
                                        $"{Attachment.Value<Int32>("weight")} opportunit{(Attachment.Value<Int32>("weight") == 1 ? "y" : "ies")} per cycle, " +
                                        $"coordinator {Attachment.Value<String>("coordinator")}",
                   "notConfigured"   => "no transport configured - give --t1s-transport <kind> or --t1s-bus <group:port>",
                   "declined"        => Attachment.Value<String>("reason") ?? "no bus to join here",
                   "cancelled"       => "cancelled",
                   _                 => Attachment.Value<String>("error") ?? "joining the bus failed - see the log"
               };

        /// <summary>
        /// How a session went, in one line.
        /// </summary>
        private static String SessionOutcome(JObject Session)
        {

            if (Session.Value<String>("outcome") != "completed")
                return Session.Value<String>("error") ?? "the session failed - see the log";

            return String.Create(CultureInfo.InvariantCulture,
                                 $"ISO 15118{Session.Value<String>("protocol")} {Session.Value<String>("mode")} " +
                                 $"with {Session.Value<String>("station")}: " +
                                 $"{Session.Value<Int32>("exchanges")} exchanges, " +
                                 $"{Session.Value<Int64>("bytesOnWire")} bytes on the wire (request side), " +
                                 $"auth {Session.Value<String>("authorization")}, " +
                                 $"setup {Session.Value<String>("sessionSetup")}, " +
                                 $"in {Session.Value<Double>("elapsed_ms") / 1000:F1} s");

        }

        /// <summary>
        /// What the pack did, in one line: the session's own words, without
        /// the "Battery:" they begin with, which the column says already.
        /// </summary>
        private static String? BatteryOutcome(JObject Battery)
        {

            const String name = "Battery: ";

            var described = Battery.Value<String>("describe");

            return described is not null && described.StartsWith(name, StringComparison.Ordinal)
                       ? described[name.Length..]
                       : described;

        }

        #endregion

    }

}
