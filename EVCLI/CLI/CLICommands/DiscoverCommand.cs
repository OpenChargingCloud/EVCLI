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

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.EV.ISO15118;

#endregion

namespace cloud.charging.open.EV.CommandLine
{

    /// <summary>
    /// Look for a charging station: an SDP request to ff02::1 on one interface.
    /// </summary>
    /// <remarks>
    /// The same thing the ISO 15118 page does with 'Look for a station', and the
    /// same thing --sdp does once at a start. All three end in
    /// <see cref="EV.DiscoverAsync"/>, which is also where the one-at-a-time
    /// rule lives - a second discovery while one is running is answered with
    /// what is happening rather than queued behind it.
    /// </remarks>
    /// <param name="CLI">The command line of the vehicle to ask.</param>
    public class DiscoverCommand(VehicleCLI CLI) : ACLICommand<VehicleCLI>(CLI),
                                                   ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as. Taken from the class name, as everywhere
        /// else: "DiscoverCommand" without its last seven characters.
        /// </summary>
        public static readonly String CommandName = nameof(DiscoverCommand)[..^7].ToLowerFirstChar();

        #endregion

        #region Suggest(Arguments)

        /// <summary>
        /// Complete the command, and then complete its one argument from the
        /// interfaces this machine actually has.
        /// </summary>
        /// <remarks>
        /// The argument is where this earns its keep. An interface is called
        /// "enp0s5" on the vehicle's Linux and "vEthernet (Default Switch)" on a
        /// Windows workstation, and nobody types either of those from memory
        /// twice. The vehicle already knows which of them could carry V2G
        /// traffic, so Tab can say it.
        ///
        /// Names with spaces in them are offered quoted, because the command
        /// line is split on whitespace unless quotes say otherwise.
        /// </remarks>
        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            #region The command itself

            if (Arguments.Length == 1)
            {

                if (CommandName.Equals    (Arguments[0], StringComparison.CurrentCultureIgnoreCase))
                    return [ SuggestionResponse.CommandCompleted(CommandName) ];

                if (CommandName.StartsWith(Arguments[0], StringComparison.CurrentCultureIgnoreCase))
                    return [ SuggestionResponse.CommandCompleted(CommandName) ];

                return [];

            }

            #endregion

            #region ... and the interface to broadcast on

            if (Arguments.Length == 2 &&
                CommandName.Equals(Arguments[0], StringComparison.CurrentCultureIgnoreCase))
            {

                var list = new List<SuggestionResponse>();

                foreach (var candidate in V2GLink.Candidates())
                {

                    if (!candidate.Name.StartsWith(Arguments[1], StringComparison.CurrentCultureIgnoreCase))
                        continue;

                    var name = candidate.Name.Contains(' ')
                                   ? $"\"{candidate.Name}\""
                                   : candidate.Name;

                    list.Add(
                        candidate.Name.Equals(Arguments[1], StringComparison.CurrentCultureIgnoreCase)
                            ? SuggestionResponse.ParameterCompleted($"{CommandName} {name}")
                            : SuggestionResponse.ParameterPrefix   ($"{CommandName} {name}")
                    );

                }

                return list;

            }

            #endregion

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override async Task<String[]> Execute(String[]           Arguments,
                                                     CancellationToken  CancellationToken)
        {

            if (Arguments.Length > 2)
                return [ $"Usage: {Help()}" ];

            var wanted = Arguments.Length == 2 ? Arguments[1] : null;

            // Said before the discovery rather than after it, and said at all
            // because the log is a log book: the SDP entries below record what
            // went out on the wire, and nothing in them says who asked for it.
            // The web interface writes the same line naming the user who
            // pressed the button; here it is whoever is at the console.
            cli.Vehicle.Log.Info(
                $"A discovery was asked for at the command line, " +
                $"{(wanted is null ? "on this vehicle's configured interface" : $"on '{wanted}'")}.",
                "15118", "sdp", "test", "cli"
            );

            var discovery = await cli.Vehicle.DiscoverAsync(
                                      wanted,
                                      CancellationToken
                                  );

            // The whole of it is in the log and on the ISO 15118 page either
            // way, so what belongs here is the one line that says how it went.
            return [ Program.DiscoveryOutcome(discovery) ];

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} [<interface>] - look for a charging station; without an interface, the configured one";

        #endregion

    }

}
