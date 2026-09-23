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

using System.Reflection;

using org.GraphDefined.Vanaheimr.CLI;

#endregion

namespace cloud.charging.open.EV.CommandLine
{

    /// <summary>
    /// The command line of a running vehicle.
    /// </summary>
    /// <remarks>
    /// Everything a command needs is reachable from here, which is why every
    /// command takes one of these: the vehicle itself, and through it its
    /// configuration, its log and everything the JSON API can do. A command is
    /// a third way of asking for the same thing, beside the web interface and
    /// the switches at a start - never an implementation of its own.
    ///
    /// Commands are not listed anywhere. The constructor asks Styx to walk this
    /// assembly for anything that implements ICLICommand and can be built from
    /// a VehicleCLI, so a new command is a new file and nothing else.
    /// </remarks>
    public class VehicleCLI : CLI
    {

        #region Properties

        /// <summary>
        /// The vehicle these commands are about.
        /// </summary>
        public EV Vehicle { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create the command line of the given vehicle.
        /// </summary>
        /// <param name="Vehicle">The running vehicle.</param>
        /// <param name="AssembliesWithCLICommands">Further assemblies to search for commands. This one is searched either way.</param>
        public VehicleCLI(EV                 Vehicle,
                          params Assembly[]  AssembliesWithCLICommands)

            : base(AssembliesWithCLICommands)

        {

            this.Vehicle = Vehicle;

            RegisterCLIType(typeof(VehicleCLI));

        }

        #endregion


        #region (protected override) GetPrompt()

        /// <summary>
        /// The vehicle's own name, because a machine that is one of several on a
        /// bench should say which one it is before it asks for a command.
        /// </summary>
        protected override String GetPrompt()

            => $"{Vehicle.VehicleName}> ";

        #endregion

    }

}
