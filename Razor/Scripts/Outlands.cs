﻿#region license

// Razor: An Ultima Online Assistant
// Copyright (C) 2021 Razor Development Community on GitHub <https://github.com/markdwags/Razor>
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.Generic;
using System.Text;
using Assistant.Core;
using Assistant.Scripts.Engine;

namespace Assistant.Scripts
{
    public static class Outlands
    {
        public static void Register()
        {
            // Lists
            Interpreter.RegisterCommandHandler("poplist", PopList);
            Interpreter.RegisterCommandHandler("pushlist", PushList);
            Interpreter.RegisterCommandHandler("removelist", RemoveList);
            Interpreter.RegisterCommandHandler("createlist", CreateList);
            Interpreter.RegisterCommandHandler("clearlist", ClearList);

            // Timers
            Interpreter.RegisterCommandHandler("settimer", SetTimer);
            Interpreter.RegisterCommandHandler("removetimer", RemoveTimer);
            Interpreter.RegisterCommandHandler("createtimer", CreateTimer);

            Interpreter.RegisterCommandHandler("getlabel", GetLabel);
            Interpreter.RegisterCommandHandler("warmode", Warmode);
            Interpreter.RegisterCommandHandler("unsetvar", UnsetVar);

            Interpreter.RegisterExpressionHandler("listexists", ListExists);
            Interpreter.RegisterExpressionHandler("list", ListLength);
            Interpreter.RegisterExpressionHandler("inlist", InList);

            Interpreter.RegisterExpressionHandler("timer", TimerValue);
            Interpreter.RegisterExpressionHandler("timerexists", TimerExists);

            Interpreter.RegisterExpressionHandler("followers", Followers);
            Interpreter.RegisterExpressionHandler("hue", Hue);

            // Mobile flags
            Interpreter.RegisterExpressionHandler("paralyzed", Paralyzed);
            Interpreter.RegisterExpressionHandler("blessed", Blessed);
            Interpreter.RegisterExpressionHandler("warmode", InWarmode);
        }

        private static bool PopList(string command, Variable[] args, bool quiet, bool force)
        {
            if (args.Length != 2)
                throw new RunTimeError("Usage: poplist ('list name') ('element value'/'front'/'back')");

            if (args[1].AsString() == "front")
            {
                if (force)
                    while (Interpreter.PopList(args[0].AsString(), true)) { }
                else
                    Interpreter.PopList(args[0].AsString(), true);
            }
            else if (args[1].AsString() == "back")
            {
                if (force)
                    while (Interpreter.PopList(args[0].AsString(), false)) { }
                else
                    Interpreter.PopList(args[0].AsString(), false);
            }
            else
            {
                if (force)
                    while (Interpreter.PopList(args[0].AsString(), args[1])) { }
                else
                    Interpreter.PopList(args[0].AsString(), args[1]);
            }

            return true;
        }

        private static bool PushList(string command, Variable[] args, bool quiet, bool force)
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RunTimeError("Usage: pushlist ('list name') ('element value') ['front'/'back']");

            bool front = false;
            if (args.Length == 3)
            {
                if (args[2].AsString() == "front")
                    front = true;
            }

            Interpreter.PushList(args[0].AsString(), args[1], front, force);

            return true;
        }

        private static bool RemoveList(string command, Variable[] args, bool quiet, bool force)
        {
            if (args.Length != 1)
                throw new RunTimeError("Usage: removelist ('list name')");

            Interpreter.DestroyList(args[0].AsString());

            return true;
        }

        private static bool CreateList(string command, Variable[] args, bool quiet, bool force)
        {
            if (args.Length != 1)
                throw new RunTimeError("Usage: createlist ('list name')");

            Interpreter.CreateList(args[0].AsString());

            return true;
        }

        private static bool ClearList(string command, Variable[] args, bool quiet, bool force)
        {
            if (args.Length != 1)
                throw new RunTimeError("Usage: clearlist ('list name')");

            Interpreter.ClearList(args[0].AsString());

            return true;
        }

        private static bool SetTimer(string command, Variable[] args, bool quiet, bool force)
        {
            if (args.Length != 2)
                throw new RunTimeError("Usage: settimer (timer name) (value)");


            Interpreter.SetTimer(args[0].AsString(), args[1].AsInt());
            return true;
        }

        private static bool RemoveTimer(string command, Variable[] args, bool quiet, bool force)
        {
            if (args.Length != 1)
                throw new RunTimeError("Usage: removetimer (timer name)");

            Interpreter.RemoveTimer(args[0].AsString());
            return true;
        }

        private static bool CreateTimer(string command, Variable[] args, bool quiet, bool force)
        {
            if (args.Length != 1)
                throw new RunTimeError("Usage: createtimer (timer name)");

            Interpreter.CreateTimer(args[0].AsString());
            return true;
        }

        private enum GetLabelState
        {
            NONE,
            WAITING_FOR_FIRST_LABEL,
            WAITING_FOR_REMAINING_LABELS
        };

        private static GetLabelState _getLabelState = GetLabelState.NONE;
        private static Action<Packet, PacketHandlerEventArgs, Serial, ushort, MessageType, ushort, ushort, string, string, string> _onLabelMessage;

        private static bool GetLabel(string command, Variable[] args, bool quiet, bool force)
        {
            if (args.Length != 2)
                throw new RunTimeError("Usage: getlabel (serial) (name)");

            var serial = args[0].AsSerial();
            var name = args[1].AsString();

            switch (_getLabelState)
            {
                case GetLabelState.NONE:
                    _getLabelState = GetLabelState.WAITING_FOR_FIRST_LABEL;
                    Interpreter.Timeout(2000, () => { return true; });

                    // Single click the object
                    Client.Instance.SendToServer(new SingleClick((Serial)args[0].AsSerial()));

                    // Capture all message responses
                    StringBuilder label = new StringBuilder();
                    _onLabelMessage = (p, a, source, graphic, type, hue, font, lang, sourceName, text) =>
                    {
                        if (source != serial)
                            return;

                        a.Block = true;

                        if (_getLabelState == GetLabelState.WAITING_FOR_FIRST_LABEL)
                        {
                            // After the first message, switch to a pause instead of a timeout.
                            _getLabelState = GetLabelState.WAITING_FOR_REMAINING_LABELS;
                            Interpreter.Pause(500);
                        }

                        label.AppendLine(text);

                        Interpreter.SetVariable(name, label.ToString(), false);
                    };

                    MessageManager.OnLabelMessage += _onLabelMessage;
                    break;
                case GetLabelState.WAITING_FOR_FIRST_LABEL:
                    break;
                case GetLabelState.WAITING_FOR_REMAINING_LABELS:
                    // We get here after the pause has expired.
                    MessageManager.OnLabelMessage -= _onLabelMessage;
                    _onLabelMessage = null;
                    _getLabelState = GetLabelState.NONE;
                    return true;
            }

            return false;
        }

        private static bool Warmode(string expression, Variable[] args, bool quiet, bool force)
        {
            if (args.Length != 1)
                throw new RunTimeError("Usage: warmode ('on' / 'off' )");

            if (args[0].AsString().ToLower() == "on")
            {
                SpecialMoves.ToggleWar();
            }
            else
            {
                SpecialMoves.TogglePeace();
            }

            return true;
        }

        private static bool UnsetVar(string expression, Variable[] args, bool quiet, bool force)
        {
            if (args.Length != 1)
                throw new RunTimeError("Usage: unsetvar ('name')");

            var name = args[0].AsString(false);

            if (force)
            {
                if (quiet)
                {
                    Interpreter.ClearVariable(name);
                }
                else
                {
                    Interpreter.ClearAlias(name);
                }
            }
            else
            {
                ScriptVariables.UnregisterVariable(name);
                ScriptManager.RedrawScripts();
            }

            return true;
        }

        private static bool ListExists(string expression, Variable[] args, bool quiet)
        {
            if (args.Length != 1)
                throw new RunTimeError("Usage: listexists ('list name')");

            if (Interpreter.ListExists(args[0].AsString()))
                return true;

            return false;
        }

        private static int ListLength(string expression, Variable[] args, bool quiet)
        {
            if (args.Length != 1)
                throw new RunTimeError("Usage: list (list name) (operator) (value)");

            return Interpreter.ListLength(args[0].AsString());
        }

        private static bool InList(string expression, Variable[] args, bool quiet)
        {
            if (args.Length != 2)
                throw new RunTimeError("Usage: inlist (list name) (element)");

            if (Interpreter.ListContains(args[0].AsString(), args[1]))
                return true;

            return false;
        }

        private static int TimerValue(string expression, Variable[] args, bool quiet)
        {
            if (args.Length != 1)
                throw new RunTimeError("Usage: timer ('timer name')");

            var ts = Interpreter.GetTimer(args[0].AsString());

            return (int)ts.TotalMilliseconds;
        }

        private static bool TimerExists(string expression, Variable[] args, bool quiet)
        {
            if (args.Length != 1)
                throw new RunTimeError("Usage: timerexists ('timer name')");

            return Interpreter.TimerExists(args[0].AsString());
        }

        private static int Followers(string expression, Variable[] args, bool quiet)
        {
            if (args.Length != 0)
                throw new RunTimeError("Usage: followers");

            return World.Player.Followers;
        }

        private static int Hue(string expression, Variable[] args, bool quiet)
        {
            if (args.Length != 1)
                throw new RunTimeError("Usage: hue ('serial')");

            var item = World.FindItem(args[0].AsSerial());

            if (item == null)
                return 0;

            return item.Hue;
        }

        private static bool Paralyzed(string expression, Variable[] args, bool quiet)
        {
            if (World.Player == null)
                return false;

            return World.Player.Paralyzed;
        }

        private static bool Blessed(string expression, Variable[] args, bool quiet)
        {
            if (World.Player == null)
                return false;

            return World.Player.Blessed;
        }

        private static bool InWarmode(string expression, Variable[] args, bool quiet)
        {
            if (World.Player == null)
                return false;

            return World.Player.Warmode;
        }
    }
}