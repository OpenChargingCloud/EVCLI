/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of EV <https://github.com/OpenChargingCloud/EV>
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

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.ISO15118.SDP.Messages;
using cloud.charging.open.protocols.ISO15118.SharedCC;
using cloud.charging.open.protocols.ISO15118.StateMachines;

using cloud.charging.open.EV.Configuration;
using cloud.charging.open.EV.ISO15118;
using cloud.charging.open.EV.Logging;
using cloud.charging.open.EV.Web;

#endregion

namespace cloud.charging.open.EV
{

    /// <summary>
    /// One electric vehicle, with its web interface, until Ctrl+C.
    /// </summary>
    /// <remarks>
    /// The switches here are this program's vocabulary and nothing else: what
    /// a vehicle is, and what it does, lives in the EV library. The one thing
    /// this file decides on its own is what to print when the vehicle is up,
    /// because that is the one moment somebody is reading a console rather
    /// than the web interface.
    ///
    /// Almost every switch below is a setting that is written to the
    /// configuration file, so it is said once rather than at every start - and
    /// so that the web interface and the command line never disagree about what
    /// this vehicle is. The exceptions are the four passwords, which are never
    /// written down, and the three switches that make something happen.
    /// </remarks>
    public class Program
    {

        #region (private static) TryTakeValue(Arguments, ref Index, out Value)

        private static Boolean TryTakeValue(String[]     Arguments,
                                            ref Int32    Index,
                                            out String?  Value)
        {

            if (Index + 1 < Arguments.Length && !Arguments[Index + 1].StartsWith("--"))
            {
                Value = Arguments[++Index];
                return true;
            }

            Value = null;
            return false;

        }

        #endregion

        #region (private static) TryTakeNumber(Arguments, ref Index, Flag, out Value)

        /// <summary>
        /// A number after a switch, or a sentence about why there is none.
        /// </summary>
        private static Boolean TryTakeNumber(String[]    Arguments,
                                             ref Int32   Index,
                                             String      Flag,
                                             out Double  Value)
        {

            if (TryTakeValue(Arguments, ref Index, out var text) &&
                Double.TryParse(text?.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out Value))
            {
                return true;
            }

            Console.Error.WriteLine($"Missing or invalid number after {Flag}!");
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
        private static Boolean TryTakeDuration(String[]      Arguments,
                                               ref Int32     Index,
                                               String        Flag,
                                               out TimeSpan  Value)
        {

            Value = TimeSpan.Zero;

            if (!TryTakeValue(Arguments, ref Index, out var raw) || raw is null)
            {
                Console.Error.WriteLine($"Missing duration after {Flag}! Try 90, 90m, 2h or 1h30m.");
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
                    Console.Error.WriteLine($"{Flag}: '{raw}' is not a duration. Try 90, 90m, 2h or 1h30m.");
                    return false;
                }

                var unit = i < text.Length ? text[i++] : 'm';

                switch (unit)
                {
                    case 's':  total += TimeSpan.FromSeconds(number);  break;
                    case 'm':  total += TimeSpan.FromMinutes(number);  break;
                    case 'h':  total += TimeSpan.FromHours  (number);  break;
                    default:
                        Console.Error.WriteLine($"{Flag}: unknown unit '{unit}' in '{raw}' - use s, m or h.");
                        return false;
                }

                seen = true;

            }

            if (!seen || total <= TimeSpan.Zero)
            {
                Console.Error.WriteLine($"{Flag} expects a duration greater than zero, got '{raw}'.");
                return false;
            }

            Value = total;
            return true;

        }

        #endregion

        #region (private static) RepositoryRoot()

        /// <summary>
        /// The directory holding EVCLI.slnx, looked up from the binary and
        /// from the current directory; the current directory when neither
        /// leads to it.
        /// </summary>
        /// <remarks>
        /// The web login file defaults to a place below it, so that it does not
        /// end up in bin/ - where the next "dotnet clean" would take the
        /// vehicle's password with it.
        /// </remarks>
        private static String RepositoryRoot()
        {

            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {

                var directory = new DirectoryInfo(start);

                while (directory is not null)
                {

                    if (File.Exists(Path.Combine(directory.FullName, "EVCLI.slnx")))
                        return directory.FullName;

                    directory = directory.Parent;

                }

            }

            return Environment.CurrentDirectory;

        }

        #endregion

        #region (private static) PrintUsage()

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: EVCLI [--port <number>] [--any] [--frontend <dist directory>]");
            Console.WriteLine("             [--web-login <file>] [--config <file>] [--verbose | --quiet] [--no-trace]");
            Console.WriteLine("             [--name <name>] [--vin <vin>]");
            Console.WriteLine("             [--battery <kWh>] [--soc <percent>] [--target-soc <percent>]");
            Console.WriteLine("             [--power <kW>] [--taper-from <percent>]");
            Console.WriteLine("             [--interface <name>] [--no-tls] [--sdp]");
            Console.WriteLine("             [--connect <host:port>] [--protocol 2|20|both] [--mode ac|dc|mcs]");
            Console.WriteLine("             [--tls | --tls-backend dotnet|bc] [--trust-roots <file|dir>]");
            Console.WriteLine("             [--pki-dir <dir>] [--vehicle-cert <pfx>] [--contract-cert <pfx>]");
            Console.WriteLine("             [--oem-cert <pfx>] [--tariff-cert <pfx>]");
            Console.WriteLine("             [--target-energy <kWh>] [--max-charging-time <dur>]");
            Console.WriteLine("             [--departure-time <dur>] [--min-soc <percent>] [--renegotiate]");
            Console.WriteLine("             [--slac-peer <host:port>] [--slac] [--charge]");
            Console.WriteLine("             [--pause | --pause-resume | --resume <hex>]");
            Console.WriteLine();
            Console.WriteLine("Web interface:");
            Console.WriteLine($"  --port <number>   TCP port to listen on (default: {EV.DefaultHTTPPort})");
            Console.WriteLine("  --any             listen on all addresses instead of 127.0.0.1");
            Console.WriteLine("  --frontend <dir>  serve the web interface from a directory on disk instead of the");
            Console.WriteLine("                    bundle embedded in the assembly - use it together with");
            Console.WriteLine("                    'npm run watch' in EV/Frontend");
            Console.WriteLine();
            Console.WriteLine("Web login:");
            Console.WriteLine($"  --web-login <file>  where the web login lives (default: {WebLoginFile.DefaultFileName} below the");
            Console.WriteLine("                      repository root). Without it a password is made up at the");
            Console.WriteLine($"                      first start for the user '{WebLoginSettings.DefaultUsername}' and shown once.");
            Console.WriteLine();
            Console.WriteLine("Configuration:");
            Console.WriteLine($"  --config <file>   where everything this vehicle is told in writing lives");
            Console.WriteLine($"                    (default: {EVConfigFile.DefaultFileName} below the repository root). Every");
            Console.WriteLine("                    setting below is written to it, so it is said once rather than at");
            Console.WriteLine("                    every start; the Configuration pages edit the same file.");
            Console.WriteLine();
            Console.WriteLine("This vehicle:");
            Console.WriteLine($"  --name <name>     what to call it (default: {VehicleConfiguration.DefaultName})");
            Console.WriteLine("  --vin <vin>       its vehicle identification number");
            Console.WriteLine($"  --battery <kWh>   the usable capacity of the pack (default: {VehicleConfiguration.DefaultCapacity_kWh:F0})");
            Console.WriteLine($"  --soc <percent>   how full it is at plug-in (default: {VehicleConfiguration.DefaultSoC_percent:F0})");
            Console.WriteLine("  --target-soc <percent>    charge until this state of charge");
            Console.WriteLine("  --power <kW>      what it asks a station for");
            Console.WriteLine($"  --taper-from <percent>    where it starts asking for less; 100 charges flat");
            Console.WriteLine($"                    (default: {VehicleConfiguration.DefaultTaperFrom_percent:F0})");
            Console.WriteLine();
            Console.WriteLine("Finding a station (SDP):");
            Console.WriteLine("  --interface <name>");
            Console.WriteLine("                    the interface the station is on, i.e. the powerline modem;");
            Console.WriteLine("                    without one the first candidate with an IPv6 link-local");
            Console.WriteLine("                    address is taken, and the console says which");
            Console.WriteLine("  --no-tls          ask stations for a plain TCP endpoint rather than a TLS one");
            Console.WriteLine();
            Console.WriteLine("The session:");
            Console.WriteLine("  --connect <host:port>");
            Console.WriteLine("                    the station to drive to, instead of looking for one. IPv6");
            Console.WriteLine("                    literals must be bracketed: [fe80::1%eth0]:15118");
            Console.WriteLine("  --protocol 2|20|both");
            Console.WriteLine("                    what to offer (default: both, -20 at priority 1). \"both\"");
            Console.WriteLine("                    offers each in one handshake and runs whichever the station");
            Console.WriteLine("                    picks, which is what a modern vehicle does");
            Console.WriteLine("  --mode ac|dc|mcs  the energy transfer mode (default: dc). Not negotiated - the");
            Console.WriteLine("                    station has to have been told the same. \"mcs\" is the DC");
            Console.WriteLine("                    message set under energy-transfer services 8/9; -20 only");
            Console.WriteLine("  --renegotiate     -2: send PowerDelivery(Renegotiate) after the first cycle");
            Console.WriteLine();
            Console.WriteLine("  Charging goals. Name none and the goal is --target-soc, or full:");
            Console.WriteLine("  --target-energy <kWh>     charge until this much has been delivered");
            Console.WriteLine("  --max-charging-time <dur> stop after this much simulated time: 90, 90m, 2h, 1h30m.");
            Console.WriteLine("                    One iteration is one simulated minute, so a full charge is");
            Console.WriteLine("                    hundreds of exchanges - give this when driving a live station");
            Console.WriteLine("  --departure-time <dur>    when the vehicle leaves. On the wire in -20, and the");
            Console.WriteLine("                    end of the session in both");
            Console.WriteLine("  --min-soc <percent>       what the driver needs by then. A floor, not a goal: it");
            Console.WriteLine("                    cannot prolong a session, and the run says whether it was met");
            Console.WriteLine();
            Console.WriteLine("TLS:");
            Console.WriteLine("  --tls             .NET SslStream. Fast and native, and on Windows and macOS");
            Console.WriteLine("                    unable to carry the -20 profile at all - see the EVCC README");
            Console.WriteLine("  --tls-backend dotnet|bc");
            Console.WriteLine("                    bc = BouncyCastle, the -20-faithful profile (TLS 1.3,");
            Console.WriteLine("                    secp521r1, mutual). Required on Windows and macOS for a real");
            Console.WriteLine("                    -20 session. Wins over --tls when both are given");
            Console.WriteLine("  --trust-roots <file|dir>");
            Console.WriteLine("                    validate the station's chain against these V2G root(s).");
            Console.WriteLine("                    Without it no station certificate is checked at all, and a");
            Console.WriteLine("                    handshake that succeeds says this vehicle was authenticated");
            Console.WriteLine("                    rather than the station");
            Console.WriteLine("  --pki-dir <dir>   the development hierarchy a station minted: this vehicle reads");
            Console.WriteLine("                    its own chain out of it and pins the station's leaf");
            Console.WriteLine();
            Console.WriteLine("This vehicle's three certificates. They are not interchangeable:");
            Console.WriteLine("  --vehicle-cert <pfx>      the Vehicle certificate - who this vehicle is. Presented");
            Console.WriteLine("                    in the TLS handshake; for -20 the station's resume binding is");
            Console.WriteLine("                    computed over it");
            Console.WriteLine("  --contract-cert <pfx>     the contract certificate - who pays. Plug & Charge");
            Console.WriteLine("  --oem-cert <pfx>  the OEM provisioning certificate - what the vehicle was born");
            Console.WriteLine("                    with. -20 asks the station to issue a contract with it and");
            Console.WriteLine("                    unwraps the key it sends back; the key must be P-521");
            Console.WriteLine("  --tariff-cert <pfx>       the public key a station's signed tariff is checked with");
            Console.WriteLine("  --vehicle-cert-pass, --contract-cert-pass, --oem-cert-pass, --tariff-cert-pass");
            Console.WriteLine("                    what opens them. Never written to the configuration file. A");
            Console.WriteLine("                    password given here stands in the process list for every other");
            Console.WriteLine("                    user of the machine, so prefer the environment:");
            Console.WriteLine($"                    {CertificatePasswords.VehicleVariable}, {CertificatePasswords.ContractVariable},");
            Console.WriteLine($"                    {CertificatePasswords.OEMVariable}, {CertificatePasswords.TariffVariable}");
            Console.WriteLine();
            Console.WriteLine("SLAC, the pairing stage before SDP:");
            Console.WriteLine("  --slac-peer <host:port>   the station's SLAC endpoint on a simulated medium. Real");
            Console.WriteLine("                    SLAC is EtherType 0x88E1 over AF_PACKET and needs Linux and");
            Console.WriteLine("                    CAP_NET_RAW; this runs the same state machine over UDP");
            Console.WriteLine();
            Console.WriteLine("Doing something at a start. Everything else above only configures:");
            Console.WriteLine("  --slac            pair once, and print what came of it");
            Console.WriteLine("  --sdp             look for a station once, and print what answered");
            Console.WriteLine("  --charge          run one session once the vehicle is up, and print the result.");
            Console.WriteLine("                    The whole exchange is in the log and on the event stream while");
            Console.WriteLine("                    it happens. The web interface does the same from a button");
            Console.WriteLine("  --pause           end that session paused rather than terminated, and print its");
            Console.WriteLine("                    identification so that another run can rejoin it");
            Console.WriteLine("  --resume <hex>    rejoin a paused session by that identification");
            Console.WriteLine("  --pause-resume    both halves in one run: charge, pause, reconnect, rejoin");
            Console.WriteLine();
            Console.WriteLine("Log:");
            Console.WriteLine("  -v, --verbose     write every entry to the console, down to the debug ones");
            Console.WriteLine("  -q, --quiet       write only warnings and worse");
            Console.WriteLine("      --no-trace    do not pick up what the libraries below write with DebugX");
            Console.WriteLine();
            Console.WriteLine("Whatever the console shows, the web interface shows the whole log under 'Logs'.");
        }

        #endregion


        public static async Task<Int32> Main(String[] Arguments)
        {

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            #region Arguments

            IPPort?  port           = null;
            var      anyAddress     = false;
            String?  frontendDir    = null;
            String?  loginFilePath  = null;
            String?  configFilePath = null;
            var      verbose        = false;
            var      quiet          = false;
            var      noTrace        = false;

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
            String?    trustRoots    = null;
            String?    pkiDir        = null;
            String?    vehicleCert   = null;
            String?    contractCert  = null;
            String?    oemCert       = null;
            String?    tariffCert    = null;
            String?    slacPeer      = null;
            Boolean?   renegotiate   = null;
            Double?    targetEnergy  = null;
            Double?    minimumSoC    = null;
            TimeSpan?  maxTime       = null;
            TimeSpan?  departure     = null;

            String?  vehicleCertPass   = null;
            String?  contractCertPass  = null;
            String?  oemCertPass       = null;
            String?  tariffCertPass    = null;

            var      pairAtStart     = false;
            var      discoverAtStart = false;
            var      chargeAtStart   = false;
            var      pause           = false;
            var      pauseResume     = false;
            String?  resumeFrom      = null;

            for (var i = 0; i < Arguments.Length; i++)
            {
                switch (Arguments[i])
                {

                    #region The web interface, the files, the log

                    case "--port":
                        if (i + 1 < Arguments.Length && UInt16.TryParse(Arguments[i + 1], out var parsedPort))
                        {
                            port = IPPort.Parse(parsedPort);
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine("Missing or invalid port number after --port!");
                            return 2;
                        }
                        break;

                    case "--any":
                        anyAddress = true;
                        break;

                    case "--frontend":
                        if (!TryTakeValue(Arguments, ref i, out frontendDir))
                        {
                            Console.Error.WriteLine("Missing directory after --frontend!");
                            return 2;
                        }
                        break;

                    case "--web-login":
                        if (!TryTakeValue(Arguments, ref i, out loginFilePath))
                        {
                            Console.Error.WriteLine("Missing file after --web-login!");
                            return 2;
                        }
                        break;

                    case "--config":
                        if (!TryTakeValue(Arguments, ref i, out configFilePath))
                        {
                            Console.Error.WriteLine("Missing file after --config!");
                            return 2;
                        }
                        break;

                    case "-v":
                    case "--verbose":
                        verbose = true;
                        break;

                    case "-q":
                    case "--quiet":
                        quiet = true;
                        break;

                    case "--no-trace":
                        noTrace = true;
                        break;

                    #endregion

                    #region What this vehicle is

                    case "--name":
                        if (!TryTakeValue(Arguments, ref i, out name))
                        {
                            Console.Error.WriteLine("Missing name after --name!");
                            return 2;
                        }
                        break;

                    case "--vin":
                        if (!TryTakeValue(Arguments, ref i, out vin))
                        {
                            Console.Error.WriteLine("Missing identification after --vin!");
                            return 2;
                        }
                        break;

                    case "--battery":
                        if (!TryTakeNumber(Arguments, ref i, "--battery", out var parsedBattery))
                            return 2;
                        battery = parsedBattery;
                        break;

                    case "--soc":
                        if (!TryTakeNumber(Arguments, ref i, "--soc", out var parsedSoC))
                            return 2;
                        soc = parsedSoC;
                        break;

                    case "--target-soc":
                        if (!TryTakeNumber(Arguments, ref i, "--target-soc", out var parsedTargetSoC))
                            return 2;
                        targetSoC = parsedTargetSoC;
                        break;

                    case "--power":
                        if (!TryTakeNumber(Arguments, ref i, "--power", out var parsedPower))
                            return 2;
                        power = parsedPower;
                        break;

                    case "--taper-from":
                        if (!TryTakeNumber(Arguments, ref i, "--taper-from", out var parsedTaper))
                            return 2;
                        taperFrom = parsedTaper;
                        break;

                    #endregion

                    #region Finding a station

                    case "--interface":
                        if (!TryTakeValue(Arguments, ref i, out interfaceName))
                        {
                            Console.Error.WriteLine("Missing interface name after --interface!");
                            return 2;
                        }
                        break;

                    case "--no-tls":
                        noTLS = true;
                        break;

                    #endregion

                    #region The session

                    case "--connect":
                        if (!TryTakeValue(Arguments, ref i, out connect))
                        {
                            Console.Error.WriteLine("Missing host:port after --connect!");
                            return 2;
                        }
                        break;

                    case "--protocol":
                        if (!TryTakeValue(Arguments, ref i, out protocolText))
                        {
                            Console.Error.WriteLine("Missing 2, 20 or both after --protocol!");
                            return 2;
                        }
                        break;

                    case "--mode":
                        if (!TryTakeValue(Arguments, ref i, out modeText))
                        {
                            Console.Error.WriteLine("Missing ac, dc or mcs after --mode!");
                            return 2;
                        }
                        break;

                    case "--renegotiate":
                        renegotiate = true;
                        break;

                    case "--target-energy":
                        if (!TryTakeNumber(Arguments, ref i, "--target-energy", out var parsedEnergy))
                            return 2;
                        targetEnergy = parsedEnergy;
                        break;

                    case "--min-soc":
                        if (!TryTakeNumber(Arguments, ref i, "--min-soc", out var parsedMinimum))
                            return 2;
                        minimumSoC = parsedMinimum;
                        break;

                    case "--max-charging-time":
                        if (!TryTakeDuration(Arguments, ref i, "--max-charging-time", out var parsedMaxTime))
                            return 2;
                        maxTime = parsedMaxTime;
                        break;

                    case "--departure-time":
                        if (!TryTakeDuration(Arguments, ref i, "--departure-time", out var parsedDeparture))
                            return 2;
                        departure = parsedDeparture;
                        break;

                    #endregion

                    #region TLS and the certificates

                    case "--tls":
                        tls = true;
                        break;

                    case "--tls-backend":
                        if (!TryTakeValue(Arguments, ref i, out tlsBackend))
                        {
                            Console.Error.WriteLine("Missing dotnet or bc after --tls-backend!");
                            return 2;
                        }
                        break;

                    case "--trust-roots":
                        if (!TryTakeValue(Arguments, ref i, out trustRoots))
                        {
                            Console.Error.WriteLine("Missing file or directory after --trust-roots!");
                            return 2;
                        }
                        break;

                    case "--pki-dir":
                        if (!TryTakeValue(Arguments, ref i, out pkiDir))
                        {
                            Console.Error.WriteLine("Missing directory after --pki-dir!");
                            return 2;
                        }
                        break;

                    case "--vehicle-cert":
                        if (!TryTakeValue(Arguments, ref i, out vehicleCert))
                        {
                            Console.Error.WriteLine("Missing PKCS#12 file after --vehicle-cert!");
                            return 2;
                        }
                        break;

                    case "--contract-cert":
                        if (!TryTakeValue(Arguments, ref i, out contractCert))
                        {
                            Console.Error.WriteLine("Missing PKCS#12 file after --contract-cert!");
                            return 2;
                        }
                        break;

                    case "--oem-cert":
                        if (!TryTakeValue(Arguments, ref i, out oemCert))
                        {
                            Console.Error.WriteLine("Missing PKCS#12 file after --oem-cert!");
                            return 2;
                        }
                        break;

                    case "--tariff-cert":
                        if (!TryTakeValue(Arguments, ref i, out tariffCert))
                        {
                            Console.Error.WriteLine("Missing PKCS#12 file after --tariff-cert!");
                            return 2;
                        }
                        break;

                    case "--vehicle-cert-pass":
                        if (!TryTakeValue(Arguments, ref i, out vehicleCertPass))
                        {
                            Console.Error.WriteLine("Missing password after --vehicle-cert-pass!");
                            return 2;
                        }
                        break;

                    case "--contract-cert-pass":
                        if (!TryTakeValue(Arguments, ref i, out contractCertPass))
                        {
                            Console.Error.WriteLine("Missing password after --contract-cert-pass!");
                            return 2;
                        }
                        break;

                    case "--oem-cert-pass":
                        if (!TryTakeValue(Arguments, ref i, out oemCertPass))
                        {
                            Console.Error.WriteLine("Missing password after --oem-cert-pass!");
                            return 2;
                        }
                        break;

                    case "--tariff-cert-pass":
                        if (!TryTakeValue(Arguments, ref i, out tariffCertPass))
                        {
                            Console.Error.WriteLine("Missing password after --tariff-cert-pass!");
                            return 2;
                        }
                        break;

                    #endregion

                    #region SLAC

                    case "--slac-peer":
                        if (!TryTakeValue(Arguments, ref i, out slacPeer))
                        {
                            Console.Error.WriteLine("Missing host:port after --slac-peer!");
                            return 2;
                        }
                        break;

                    #endregion

                    #region What to actually do at a start

                    case "--slac":
                        pairAtStart = true;
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
                        if (!TryTakeValue(Arguments, ref i, out resumeFrom))
                        {
                            Console.Error.WriteLine("Missing session identification after --resume!");
                            return 2;
                        }
                        chargeAtStart = true;
                        break;

                    #endregion

                    case "-h":
                    case "--help":
                        PrintUsage();
                        return 0;

                    default:
                        Console.Error.WriteLine($"Unknown argument '{Arguments[i]}'!");
                        PrintUsage();
                        return 2;

                }
            }

            if (verbose && quiet)
            {
                Console.Error.WriteLine("--verbose and --quiet ask for opposite things!");
                return 2;
            }

            if (pause && pauseResume)
            {
                Console.Error.WriteLine("--pause ends the session paused and stops there; --pause-resume goes on to rejoin it. " +
                                        "They ask for different runs.");
                return 2;
            }

            #endregion

            #region Where the web interface comes from

            // A directory given on the command line wins, so that
            // "npm run watch" beside a running vehicle shows up in the browser
            // on a reload, without rebuilding the C# side.
            IStaticContentSource? frontend = null;

            if (frontendDir is not null)
            {

                if (!Directory.Exists(frontendDir))
                {
                    Console.Error.WriteLine($"The frontend directory '{frontendDir}' does not exist!");
                    return 2;
                }

                frontend = new FileSystemContentSource(frontendDir);

            }

            #endregion

            #region The vehicle

            EV vehicle;

            try
            {
                vehicle = new EV(

                              HTTPHostname:     anyAddress
                                                    ? IPvXAddress.Any
                                                    : IPv4Address.Localhost,

                              HTTPPort:         port,

                              LoginFile:        new WebLoginFile(
                                                    loginFilePath ?? Path.Combine(RepositoryRoot(), WebLoginFile.DefaultFileName)
                                                ),

                              ConfigFile:       new EVConfigFile(
                                                    configFilePath ?? Path.Combine(RepositoryRoot(), EVConfigFile.DefaultFileName)
                                                ),

                              Frontend:         frontend,

                              // Only what was typed. Everything else the
                              // environment holds is filled in by the vehicle,
                              // and neither is ever written down.
                              Passwords:        new CertificatePasswords(
                                                    vehicleCertPass,
                                                    contractCertPass,
                                                    oemCertPass,
                                                    tariffCertPass
                                                ),

                              ConsoleLogLevel:  verbose ? LogLevel.Debug
                                                    : quiet ? LogLevel.Warning
                                                    : LogLevel.Info,

                              BridgeDebugLog:   !noTrace

                          );
            }
            catch (Exception e)
            {

                Console.Error.WriteLine($"The electric vehicle could not be set up: {e.Message}");

                // A vehicle that does not come up at all is the one moment the
                // stack trace is worth more than a tidy console.
                if (verbose)
                    Console.Error.WriteLine(e);

                return 1;

            }

            #endregion

            await using (vehicle)
            {

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
                        Console.Error.WriteLine($"The vehicle could not be configured: {problem}");
                        return 2;
                    }

                }

                if (interfaceName is not null || noTLS)
                {

                    var told = new V2GConfiguration(
                                   InterfaceName:      interfaceName,
                                   RequestedSecurity:  noTLS ? SDP_Security.NoTLS : null
                               ).ToJSON();

                    if (!vehicle.TryUpdateV2GConfiguration(told, out var problem))
                    {
                        Console.Error.WriteLine($"The link could not be configured: {problem}");
                        return 2;
                    }

                }

                #endregion

                #region What the switches said about a session

                if (connect is not null || protocolText is not null || modeText is not null || tls ||
                    tlsBackend is not null || trustRoots is not null || pkiDir is not null ||
                    vehicleCert is not null || contractCert is not null || oemCert is not null ||
                    tariffCert is not null || slacPeer is not null || renegotiate.HasValue ||
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
                    if (trustRoots   is not null)  told["trustRoots"]                   = trustRoots;
                    if (pkiDir       is not null)  told["pkiDirectory"]                 = pkiDir;
                    if (vehicleCert  is not null)  told["vehicleCertificate"]           = vehicleCert;
                    if (contractCert is not null)  told["contractCertificate"]          = contractCert;
                    if (oemCert      is not null)  told["oemCertificate"]               = oemCert;
                    if (tariffCert   is not null)  told["tariffCertificate"]            = tariffCert;
                    if (slacPeer     is not null)  told["slacPeer"]                     = slacPeer;
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
                    {
                        Console.Error.WriteLine($"The session could not be configured: {problem}");
                        return 2;
                    }

                }

                #endregion

                try
                {
                    await vehicle.Start();
                }
                catch (PortUnavailableException problem)
                {

                    // What somebody starting a second copy of this vehicle used
                    // to get was thirteen frames of stack trace under the
                    // operating system's own words for a port in use - in
                    // German on a German Windows, under eleven lines of English
                    // log, with the port named nowhere.
                    Console.Error.WriteLine($"The electric vehicle could not start: {problem.Message}.");
                    Console.Error.WriteLine("Another copy of this vehicle already running is the usual answer. " +
                                            "Stop it, or give this one another port with --port <number>.");

                    if (verbose)
                        Console.Error.WriteLine(problem);

                    return 1;

                }

                #region What somebody who just started this needs to know

                var candidates = V2GLink.Candidates();
                var session    = vehicle.SessionSettings;

                Console.WriteLine();
                Console.WriteLine($"  web interface  {vehicle.WebInterfaceURL}");
                Console.WriteLine($"  JSON API       {vehicle.WebInterfaceURL}api/v1/status");
                Console.WriteLine($"  event stream   {vehicle.WebInterfaceURL}api/v1/events");
                Console.WriteLine($"  frontend from  {vehicle.Frontend.Description}");
                Console.WriteLine($"  web login      user '{vehicle.Sessions.Username}', {vehicle.LoginFile.Path}");
                Console.WriteLine($"  configuration  {vehicle.ConfigFile.Path}");
                Console.WriteLine($"  vehicle        {vehicle.VehicleName}{(vehicle.VIN is not null ? $" ({vehicle.VIN})" : "")}");
                Console.WriteLine($"  battery        {vehicle.StateOfCharge_percent:F0} % of {vehicle.BatteryCapacity_kWh:F0} kWh, " +
                                  $"asking for {vehicle.MaxChargingPower_kW:F1} kW up to {vehicle.TargetStateOfCharge_percent:F0} %");
                Console.WriteLine($"  name servers   {(vehicle.DNSEnabled ? String.Join(", ", vehicle.DNSClient.DNSServers) : "switched off")}");
                Console.WriteLine($"  time server    {vehicle.NTSClient.Hostname}{(vehicle.NTSEnabled ? "" : " (switched off)")}");
                Console.WriteLine($"  V2G interface  {vehicle.V2GSettings.InterfaceName ?? "whichever one comes first"}" +
                                  (candidates.Count > 0
                                       ? $" (of {String.Join(", ", candidates.Select(candidate => candidate.Name))})"
                                       : " - and this machine has none that could carry V2G traffic"));
                Console.WriteLine($"  station        {session.Connect ?? "whichever one answers an SDP request"}");
                Console.WriteLine($"  session        {session.ProtocolWritten ?? "both"}, " +
                                  $"{(session.ModeWritten ?? "dc").ToUpperInvariant()}, " +
                                  $"TLS {session.TLSWritten ?? "none"}" +
                                  (session.SLACPeer is not null ? $", SLAC over {session.SLACPeer}" : ""));

                if (vehicle.GeneratedPassword is not null)
                {
                    Console.WriteLine();
                    Console.WriteLine("  ┌─ First start: there was no web login, so one was made up for you ─────────");
                    Console.WriteLine($"  │  user      {vehicle.Sessions.Username}");
                    Console.WriteLine($"  │  password  {vehicle.GeneratedPassword}");
                    Console.WriteLine("  │  It is shown here once and kept only as a hash. Write it down.");
                    Console.WriteLine("  └───────────────────────────────────────────────────────────────────────────");
                }

                Console.WriteLine();

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

                    if (charged["battery"] is JObject pack && pack["describe"]?.Type == JTokenType.String)
                        Console.WriteLine($"  battery        {pack.Value<String>("describe")}");

                    if (charged.Value<String>("pausedSessionId") is { } pausedId)
                        Console.WriteLine($"  paused as      {pausedId}  (rejoin it with --resume {pausedId})");

                    Console.WriteLine();

                }

                #endregion

                Console.WriteLine("Press Ctrl+C to stop.");
                Console.WriteLine();

                #region Wait for Ctrl+C

                var stopped = new TaskCompletionSource();

                Console.CancelKeyPress += (_, e) => {
                    e.Cancel = true;
                    stopped.TrySetResult();
                };

                await stopped.Task;

                #endregion

            }

            return 0;

        }


        #region (private static) DiscoveryOutcome(Discovery) / PairOutcome(Pairing) / SessionOutcome(Session)

        /// <summary>
        /// How a discovery went, in one line for the console. The whole of it
        /// is in the log either way.
        /// </summary>
        private static String DiscoveryOutcome(JObject Discovery)
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
        /// How a session went, in one line.
        /// </summary>
        private static String SessionOutcome(JObject Session)
        {

            if (Session.Value<String>("outcome") != "completed")
                return Session.Value<String>("error") ?? "the session failed - see the log";

            return $"ISO 15118{Session.Value<String>("protocol")} {Session.Value<String>("mode")} " +
                   $"with {Session.Value<String>("station")}: " +
                   $"{Session.Value<Int32>("exchanges")} exchanges, " +
                   $"{Session.Value<Int64>("bytesOnWire")} bytes on the wire (request side), " +
                   $"auth {Session.Value<String>("authorization")}, " +
                   $"setup {Session.Value<String>("sessionSetup")}, " +
                   $"in {Session.Value<Double>("elapsed_ms") / 1000:F1} s";

        }

        #endregion

    }

}
