#region license

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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Assistant.Gumps.Internal;
using Assistant.Macros;
using Assistant.Scripts.Engine;
using Assistant.UI;
using FastColoredTextBoxNS;

namespace Assistant.Scripts
{
    public static class ScriptManager
    {
        public static bool Recording { get; set; }

        public static bool Running => ScriptRunning;

        private static bool ScriptRunning { get; set; }

        public static DateTime LastWalk { get; set; }

        public static bool SetLastTargetActive { get; set; }
        
        public static bool TargetFound { get; set; }

        public static string ScriptPath => Config.GetUserDirectory("Scripts");

        private static FastColoredTextBox ScriptEditor { get; set; }

        private static TreeView ScriptTree { get; set; }

        private static ListBox ScriptVariableList { get; set; }

        private static Script _queuedScript;

        public static bool BlockPopupMenu { get; set; }

        public enum HighlightType
        {
            Error,
            Execution
        }

        private static Dictionary<HighlightType, List<int>> HighlightLines { get; } = new Dictionary<HighlightType, List<int>>();

        private static Dictionary<HighlightType, Brush> HighlightLineColors { get; } = new Dictionary<HighlightType, Brush>()
        {
            { HighlightType.Error, new SolidBrush(Color.Red) },
            { HighlightType.Execution, new SolidBrush(Color.Blue) }
        };

        private static HighlightType[] GetHighlightTypes()
        {
            return (HighlightType[])Enum.GetValues(typeof(HighlightType));
        }

        public static RazorScript SelectedScript { get; set; }

        public static bool PopoutEditor { get; set; }

        public static void Tick()
        {
            try
            {
                if (!Client.Instance.ClientRunning)
                {
                    if (ScriptRunning)
                    {
                        ScriptRunning = false;
                        Interpreter.StopScript();
                    }

                    return;
                }

                bool running;

                if (_queuedScript != null)
                {
                    // Starting a new script. This relies on the atomicity for references in CLR
                    var script = _queuedScript;

                    running = Interpreter.StartScript(script);
                    UpdateLineNumber(Interpreter.CurrentLine);

                    _queuedScript = null;
                }
                else
                {
                    running = Interpreter.ExecuteScript();
                    if (running)
                        UpdateLineNumber(Interpreter.CurrentLine);
                }


                if (running)
                {
                    if (Running == false)
                    {
                        if (Config.GetBool("ScriptDisablePlayFinish"))
                            World.Player?.SendMessage(LocString.ScriptPlaying);

                        Assistant.Engine.MainWindow.LockScriptUI(true);
                        Assistant.Engine.RazorScriptEditorWindow?.LockScriptUI(true);
                        ScriptRunning = true;
                    }
                }
                else
                {
                    if (Running)
                    {
                        if (Config.GetBool("ScriptDisablePlayFinish"))
                            World.Player?.SendMessage(LocString.ScriptFinished);

                        Assistant.Engine.MainWindow.LockScriptUI(false);
                        Assistant.Engine.RazorScriptEditorWindow?.LockScriptUI(false);
                        ScriptRunning = false;

                        ClearHighlightLine(HighlightType.Execution);
                    }
                }
            }
            catch (Exception ex)
            {
                World.Player?.SendMessage(MsgLevel.Error, $"Script Error: {ex.Message} (Line: {Interpreter.CurrentLine + 1})");

                SetHighlightLine(Interpreter.CurrentLine, HighlightType.Error);

                StopScript();
            }
        }

        /// <summary>
        /// This is called via reflection when the application starts up
        /// </summary>
        public static void Initialize()
        {
            HotKey.Add(HKCategory.Scripts, HKSubCat.None, LocString.StopScript, HotkeyStopScript);
            HotKey.Add(HKCategory.Scripts, HKSubCat.None, LocString.ScriptDClickType, HotkeyDClickTypeScript);
            HotKey.Add(HKCategory.Scripts, HKSubCat.None, LocString.ScriptTargetType, HotkeyTargetTypeScript);

            _scriptList = new List<RazorScript>();

            Recurse(null, Config.GetUserDirectory("Scripts"));

            foreach (HighlightType type in GetHighlightTypes())
            {
                HighlightLines[type] = new List<int>();
            }
        }

        private static void HotkeyTargetTypeScript()
        {
            if (World.Player != null)
            {
                World.Player.SendMessage(MsgLevel.Force, LocString.ScriptTargetType);
                Targeting.OneTimeTarget(OnTargetTypeScript);
            }
        }

        private static void OnTargetTypeScript(bool loc, Serial serial, Point3D pt, ushort itemId)
        {
            Item item = World.FindItem(serial);

            if (item != null && item.Serial.IsItem && item.Movable && item.Visible)
            {
                string cmd = $"targettype '{item.ItemID.ItemData.Name}'";

                Clipboard.SetDataObject(cmd);
                World.Player.SendMessage(MsgLevel.Force, Language.Format(LocString.ScriptCopied, cmd), false);
            }
            else
            {
                Mobile m = World.FindMobile(serial);

                if (m != null)
                {
                    string cmd = $"targettype '{m.Body}'";

                    Clipboard.SetDataObject(cmd);
                    World.Player.SendMessage(MsgLevel.Force, Language.Format(LocString.ScriptCopied, cmd), false);
                }
            }
        }

        private static void HotkeyDClickTypeScript()
        {
            if (World.Player != null)
            {
                World.Player.SendMessage(MsgLevel.Force, LocString.ScriptTargetType);
                Targeting.OneTimeTarget(OnDClickTypeScript);
            }
        }

        private static void OnDClickTypeScript(bool loc, Serial serial, Point3D pt, ushort itemId)
        {
            Item item = World.FindItem(serial);

            if (item != null && item.Serial.IsItem && item.Movable && item.Visible)
            {
                string cmd = $"dclicktype '{item.ItemID.ItemData.Name}'";

                Clipboard.SetDataObject(cmd);
                World.Player.SendMessage(MsgLevel.Force, Language.Format(LocString.ScriptCopied, cmd), false);
            }
            else
            {
                Mobile m = World.FindMobile(serial);

                if (m != null)
                {
                    string cmd = $"dclicktype '{m.Body}'";

                    Clipboard.SetDataObject(cmd);
                    World.Player.SendMessage(MsgLevel.Force, Language.Format(LocString.ScriptCopied, cmd), false);
                }
            }
        }

        private static void HotkeyStopScript()
        {
            StopScript();
        }

        private static void AddHotkey(RazorScript script)
        {
            HotKey.Add(HKCategory.Scripts, HKSubCat.None, Language.Format(LocString.PlayScript, script), OnHotKey,
                script);
        }

        private static void RemoveHotkey(RazorScript script)
        {
            HotKey.Remove(Language.Format(LocString.PlayScript, script.ToString()));
        }

        public static void OnHotKey(ref object state)
        {
            RazorScript script = (RazorScript) state;

            PlayScript(script.Lines);
        }

        public static void StopScript()
        {
            _queuedScript = null;

            Interpreter.StopScript();
        }

        public static RazorScript AddScript(string file)
        {
            RazorScript script = new RazorScript
            {
                Lines = File.ReadAllLines(file),
                Name = Path.GetFileNameWithoutExtension(file),
                Path = file
            };

            if (Path.GetDirectoryName(script.Path).Equals(Config.GetUserDirectory("Scripts")))
            {
                script.Category = string.Empty;
            }
            else
            {
                string cat = file.Replace(Config.GetUserDirectory("Scripts"), "").Substring(1);
                script.Category = Path.GetDirectoryName(cat).Replace("/", "\\");
            }

            AddHotkey(script);

            _scriptList.Add(script);

            return script;
        }

        public static void RemoveScript(RazorScript script)
        {
            RemoveHotkey(script);

            _scriptList.Remove(script);
        }
        
        public static void PlayScript(string scriptName)
        {
            foreach (RazorScript razorScript in _scriptList)
            {
                if (razorScript.ToString().IndexOf(scriptName, StringComparison.OrdinalIgnoreCase) != -1)
                {
                    PlayScript(razorScript.Lines);
                    break;
                }
            }
        }

        public static void PlayScript(string[] lines)
        {
            if (World.Player == null || ScriptEditor == null || lines == null)
                return;

            ClearAllHighlightLines();

            if (MacroManager.Playing || MacroManager.StepThrough)
                MacroManager.Stop();

            StopScript(); // be sure nothing is running

            SetLastTargetActive = false;

            if (_queuedScript != null)
                return;

            if (!Client.Instance.ClientRunning)
                return;

            if (World.Player == null)
                return;

            Script script = new Script(Lexer.Lex(lines));

            _queuedScript = script;
        }

        private static void UpdateLineNumber(int lineNum)
        {
            if (PopoutEditor)
            {
                SetHighlightLine(lineNum, HighlightType.Execution);
                // Scrolls to relevant line, per this suggestion: https://github.com/PavelTorgashov/FastColoredTextBox/issues/115
                ScriptEditor.Selection.Start = new Place(0, lineNum);
                ScriptEditor.DoSelectionVisible();
            }
        }

        public static void SetEditor(FastColoredTextBox scriptEditor, bool popoutEditor)
        {
            ScriptEditor = scriptEditor;
            ScriptEditor.Visible = true;

            PopoutEditor = popoutEditor;

            InitScriptEditor();

            if (SelectedScript != null)
            {
                SetEditorText(SelectedScript);
            }
        }

        public static void SetEditorText(RazorScript selectedScript)
        {
            SelectedScript = selectedScript;

            ScriptEditor.Text = string.Join("\n", SelectedScript.Lines);
        }

        public static void SetControls(FastColoredTextBox scriptEditor, TreeView scriptTree, ListBox scriptVariables)
        {
            ScriptEditor = scriptEditor;
            ScriptTree = scriptTree;
            ScriptVariableList = scriptVariables;
        }

        public static void OnLogin()
        {
            Commands.Register();
            AgentCommands.Register();
            SpeechCommands.Register();
            TargetCommands.Register();

            Aliases.Register();
            Expressions.Register();

            Outlands.Register();
        }

        public static void OnLogout()
        {
            StopScript();
            Assistant.Engine.MainWindow.LockScriptUI(false);
            Assistant.Engine.RazorScriptEditorWindow.LockScriptUI(false);
        }

        private static List<RazorScript> _scriptList { get; set; }

        public static void RedrawScriptVariables()
        {
            ScriptVariableList?.SafeAction(s =>
            {
                s.BeginUpdate();
                s.Items.Clear();

                foreach (var kv in ScriptVariables.Variables)
                {
                    s.Items.Add($"{kv.Key} ({kv.Value})");
                }

                s.EndUpdate();
                s.Refresh();
                s.Update();
            });
        }

        public static bool AddToScript(string command)
        {
            if (Recording)
            {
                ScriptEditor?.AppendText(command + Environment.NewLine);

                return true;
            }

            return false;
        }

        private static int GetScriptIndex(string script)
        {
            for (int i = 0; i < _scriptList.Count; i++)
            {
                if (_scriptList[i].Name.Equals(script, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        public static void Error(bool quiet, string statement, string message, bool throwError = false)
        {
            if (quiet)
                return;

            World.Player?.SendMessage(MsgLevel.Error, $"{statement}: {message}");
        }

        public static List<ASTNode> ParseArguments(ref ASTNode node)
        {
            List<ASTNode> args = new List<ASTNode>();
            while (node != null)
            {
                args.Add(node);
                node = node.Next();
            }

            return args;
        }

        private delegate void SetHighlightLineDelegate(int iline, Color color);

        /// <summary>
        /// Adds a new highlight of specified type
        /// </summary>
        /// <param name="iline">Line number to highlight</param>
        /// <param name="type">Type of highlight to set</param>
        private static void AddHighlightLine(int iline, HighlightType type)
        {
            HighlightLines[type].Add(iline);
            RefreshHighlightLines();
        }

        /// <summary>
        /// Clears existing highlight lines of this type, and adds a new one at specified line number
        /// </summary>
        /// <param name="iline">Line number to highlight</param>
        /// <param name="type">Type of highlight to set</param>
        private static void SetHighlightLine(int iline, HighlightType type)
        {
            if (!PopoutEditor)
                return;

            ClearHighlightLine(type);
            AddHighlightLine(iline, type);
        }

        public static void ClearHighlightLine(HighlightType type)
        {
            if (!PopoutEditor)
                return;

            HighlightLines[type].Clear();
            RefreshHighlightLines();
        }

        public static void ClearAllHighlightLines()
        {
            if (!PopoutEditor)
                return;

            foreach (HighlightType type in GetHighlightTypes())
            {
                HighlightLines[type].Clear();
            }

            RefreshHighlightLines();
        }

        private static void RefreshHighlightLines()
        {
            for (int i = 0; i < ScriptEditor.LinesCount; i++)
            {
                ScriptEditor[i].BackgroundBrush = ScriptEditor.BackBrush;
            }

            foreach (HighlightType type in GetHighlightTypes())
            {
                foreach (int lineNum in HighlightLines[type])
                {
                    ScriptEditor[lineNum].BackgroundBrush = HighlightLineColors[type];
                }
            }

            ScriptEditor.Invalidate();
        }

        private static FastColoredTextBoxNS.AutocompleteMenu _autoCompleteMenu;

        public static void InitScriptEditor()
        {
            _autoCompleteMenu = new AutocompleteMenu(ScriptEditor)
            {
                SearchPattern = @"[\w\.:=!<>]",
                AllowTabKey = true,
                ToolTipDuration = 5000,
                AppearInterval = 100
            };

            #region Keywords

            string[] keywords =
            {
                    "if", "elseif", "else", "endif", "while", "endwhile", "for", "endfor", "break", "continue", "stop",
                    "replay", "not", "and", "or",
                    "foreach", "as", "in"
                };

            #endregion

            #region Commands auto-complete

            string[] commands =
            {
                    "attack", "cast", "dclick", "dclicktype", "dress", "drop", "droprelloc", "gumpresponse", "gumpclose",
                    "hotkey", "lasttarget", "lift", "lifttype", "menu", "menuresponse", "organizer", "overhead", "potion",
                    "promptresponse", "restock", "say", "whisper", "yell", "emote", "script", "scavenger", "sell", "setability",
                    "setlasttarget",
                    "setvar", "skill", "sysmsg", "target", "targettype", "targetrelloc", "undress", "useonce", "walk",
                    "wait", "pause", "waitforgump", "waitformenu", "waitforprompt", "waitfortarget", "clearsysmsg", "clearjournal",
                    "waitforsysmsg", "clearhands", "clearall", "virtue", "random",
                    "warmode", "getlabel", "createlist", "clearlist", "removelist", "pushlist", "poplist", "createtimer", "removetimer", "settimer",
                    "unsetvar", "ignore", "clearignore", "rename", "setskill", "noto", "ingump", "gumpexists", "dead", "invul", "paralyzed",
                    "counttype", "diffmana", "diffstam", "diffhits", "diffweight", "maxweight", "targetexists", "find", "findlayer", "name",
                    "followers", "hue", "timerexists", "timer",
                };

            #endregion

            Dictionary<string, ToolTipDescriptions> descriptionCommands = new Dictionary<string, ToolTipDescriptions>();

            #region CommandToolTips

            var tooltip = new ToolTipDescriptions("attack", new[] { "attack (serial) or attack ('variablename')" },
                "N/A", "Attack a specific serial or variable tied to a serial.", "attack 0x2AB4\n\tattack 'attackdummy'");
            descriptionCommands.Add("attack", tooltip);

            tooltip = new ToolTipDescriptions("clearall", new[] { "clearall" }, "N/A", "Clear target, clear queues, drop anything you're holding",
                "clearall");
            descriptionCommands.Add("clearall", tooltip);

            tooltip = new ToolTipDescriptions("clearhands", new[] { "clearhands ('right'/'left'/'hands')" }, "N/A", "Use the item in your hands",
                "clearhands");
            descriptionCommands.Add("clearhands", tooltip);

            tooltip = new ToolTipDescriptions("virtue", new[] { "virtue ('honor'/'sacrifice'/'valor')" }, "N/A", "Invoke a specific virtue",
                "virtue 'honor'");
            descriptionCommands.Add("virtue", tooltip);

            tooltip = new ToolTipDescriptions("cast", new[] { "cast ('name of spell')" }, "N/A", "Cast a spell by name",
                "cast 'blade spirits'");
            descriptionCommands.Add("cast", tooltip);

            tooltip = new ToolTipDescriptions("dclick", new[] { "dclick (serial) or useobject (serial)" }, "N/A",
                "This command will use (double-click) a specific item or mobile.", "dclick 0x34AB");
            descriptionCommands.Add("dclick", tooltip);

            tooltip = new ToolTipDescriptions("dclicktype",
                new[]
                {
                        "dclicktype ('name') OR ('graphic') [source] [hue] [quantity] [range]"
                }, "N/A",
                "This command will use (double-click) an item type either provided by the name or the graphic ID.\n\tYou can specify the source, color and search distance (or depth)",
                "dclicktype 'dagger' 'backpack' 'any' 1 2\n\t\twaitfortarget\n\t\ttargettype 'robe'");
            descriptionCommands.Add("dclicktype", tooltip);

            tooltip = new ToolTipDescriptions("findtype",
                new[]
                {
                    "findtype ('name') OR ('graphic') [source] [hue] [quantity] [range]"
                }, "N/A",
                "This expression will find item or mobile by type either provided by the name or the graphic ID.\n\tYou can specify the source, color and search distance (or depth)",
                "findtype 'an eagle' 'ground' 31337 1 2");
            descriptionCommands.Add("findtype", tooltip);

            tooltip = new ToolTipDescriptions("find",
                new[]
                {
                    "find (serial) [src] [hue] [qty] [range]"
                }, "True if mobile or item with given serial was found",
                "Check if mobile or item with given has been found.\n\tYou can specify the source, color and search distance (or depth)",
                "find 'an eagle' 'ground' 31337 1 2");
            descriptionCommands.Add("find", tooltip);

            tooltip = new ToolTipDescriptions("findlayer",
                new[]
                {
                    "findlayer (serial) (layer)"
                }, "Serial of founded item in passed layer, otherwise zero",
                "Check if mobile with specified serial have item on a passed layer name.",
                "if 'targetMe' 'ring'\n\tsay 'Nice ring'\nendif");
            descriptionCommands.Add("findlayer", tooltip);

            tooltip = new ToolTipDescriptions("dress", new[] { "dress ('name of dress list')" }, "N/A",
                "This command will execute a spec dress list you have defined in Razor.", "dress 'My Sunday Best'");
            descriptionCommands.Add("dress", tooltip);

            tooltip = new ToolTipDescriptions("drop", new[] { "drop (serial) (x/y/z/layername)" }, "N/A",
                "This command will drop the item you are holding either at your feet,\n\t\ton a specific layer or at a specific X / Y / Z location.",
                "lift 0x400D54A7 1\n\t\tdrop 0x6311 InnerTorso");
            descriptionCommands.Add("drop", tooltip);

            tooltip = new ToolTipDescriptions("", new[] { "" }, "N/A", "",
                "lift 0x400D54A7 1\n\twait 5000\n\tdrop 0xFFFFFFFF 5926 1148 0");
            descriptionCommands.Add("", tooltip);

            tooltip = new ToolTipDescriptions("droprelloc", new[] { "droprelloc (x) (y)" }, "N/A",
                "This command will drop the item you're holding to a location relative to your position.",
                "lift 0x400EED2A 1\n\twait 1000\n\tdroprelloc 1 1");
            descriptionCommands.Add("droprelloc", tooltip);

            tooltip = new ToolTipDescriptions("gumpresponse", new[] { "gumpresponse (buttonID)" }, "N/A",
                "Responds to a specific gump button", "gumpresponse 4");
            descriptionCommands.Add("gumpresponse", tooltip);

            tooltip = new ToolTipDescriptions("gumpclose", new[] { "gumpclose" }, "N/A",
                "This command will close the last gump that opened.", "gumpclose");
            descriptionCommands.Add("gumpclose", tooltip);

            tooltip = new ToolTipDescriptions("hotkey", new[] { "hotkey ('name of hotkey')" }, "N/A",
                "This command will execute any Razor hotkey by name.",
                "skill 'detect hidden'\n\twaitfortarget\n\thotkey 'target self'");
            descriptionCommands.Add("hotkey", tooltip);

            tooltip = new ToolTipDescriptions("lasttarget", new[] { "lasttarget" }, "N/A",
                "This command will target your last target set in Razor.",
                "cast 'magic arrow'\n\twaitfortarget\n\tlasttarget");
            descriptionCommands.Add("lasttarget", tooltip);

            tooltip = new ToolTipDescriptions("lift", new[] { "lift (serial) [amount]" }, "N/A",
                "This command will lift a specific item and amount. If no amount is provided, 1 is defaulted.",
                "lift 0x400EED2A 1\n\twait 1000\n\tdroprelloc 1 1 0");
            descriptionCommands.Add("lift", tooltip);

            tooltip = new ToolTipDescriptions("lifttype",
                new[] { "lifttype (name) OR (graphic) [amount] [src] [hue]" }, "N/A",
                "This command will lift a specific item by type either by the graphic id or by the name.\n\tIf no amount is provided, 1 is defaulted.\n\tSrc parameter can be 'ground','self' or 'backpack'",
                "lifttype 'robe'\n\twait 1000\n\tdroprelloc 1 1 0\n\tlifttype 0x1FCD\n\twait 1000\n\tdroprelloc 1 1");
            descriptionCommands.Add("lifttype", tooltip);

            tooltip = new ToolTipDescriptions("menu", new[] { "menu (serial) (index) [false]" }, "N/A",
                "Selects a specific index within a context menu", "# open backpack\n\tmenu 0 1");
            descriptionCommands.Add("menu", tooltip);

            tooltip = new ToolTipDescriptions("menuresponse", new[] { "menuresponse (index) (menuId) [hue]" }, "N/A",
                "Responds to a specific menu and menu ID (not a context menu)", "menuresponse 3 4");
            descriptionCommands.Add("menuresponse", tooltip);

            tooltip = new ToolTipDescriptions("organizer", new[] { "organizer (number) ['set']" }, "N/A",
                "This command will execute a specific organizer agent. If the set parameter is included,\n\tyou will instead be prompted to set the organizer agent's hotbag.",
                "organizer 1\n\torganizer 4 'set'");
            descriptionCommands.Add("organizer", tooltip);

            tooltip = new ToolTipDescriptions("overhead", new[] { "overhead ('text') [color] [serial]" }, "N/A",
                "This command will display a message over your head. Only you can see this.",
                "if stam = 100\n\t    overhead 'ready to go!'\n\tendif");
            descriptionCommands.Add("overhead", tooltip);

            tooltip = new ToolTipDescriptions("potion", new[] { "potion ('potion type')" }, "N/A",
                "This command will use a specific potion based on the type.", "potion 'agility'\n\tpotion 'heal'");
            descriptionCommands.Add("potion", tooltip);

            tooltip = new ToolTipDescriptions("promptresponse", new[] { "promptresponse ('prompt response')" }, "N/A",
                "This command will respond to a prompt triggered from actions such as renaming runes or giving a guild title.",
                "dclicktype 'rune'\n\twaitforprompt\n\tpromptresponse 'to home'");
            descriptionCommands.Add("promptresponse", tooltip);

            tooltip = new ToolTipDescriptions("restock", new[] { "restock (number) ['set']" }, "N/A",
                "This command will execute a specific restock agent.\n\tIf the set parameter is included, you will instead be prompted to set the restock agent's hotbag.",
                "restock 1\n\trestock 4 'set'");
            descriptionCommands.Add("restock", tooltip);

            tooltip = new ToolTipDescriptions("say",
                new[] { "say ('message to send') [hue] or msg ('message to send') [hue]" }, "N/A",
                "This command will force your character to say the message passed as the parameter.",
                "say 'Hello world!'\n\tsay 'Hello world!' 454");
            descriptionCommands.Add("say", tooltip);

            tooltip = new ToolTipDescriptions("whisper",
                new[] { "whisper ('message to send') [hue]" }, "N/A",
                "This command will force your character to whisper the message passed as the parameter.",
                "whisper 'Hello world!'\n\twhisper 'Hello world!' 454");
            descriptionCommands.Add("whisper", tooltip);

            tooltip = new ToolTipDescriptions("yell",
                new[] { "yell ('message to send') [hue]" }, "N/A",
                "This command will force your character to yell the message passed as the parameter.",
                "yell 'Hello world!'\n\tyell 'Hello world!' 454");
            descriptionCommands.Add("yell", tooltip);

            tooltip = new ToolTipDescriptions("emote",
                new[] { "emote ('message to send') [hue]" }, "N/A",
                "This command will force your character to emote the message passed as the parameter.",
                "emote 'Hello world!'\n\temote 'Hello world!' 454");
            descriptionCommands.Add("emote", tooltip);

            tooltip = new ToolTipDescriptions("script", new[] { "script 'name'" }, "N/A",
                "This command will call another script.", "if hp = 40\n\t   script 'healself'\n\tendif");
            descriptionCommands.Add("script", tooltip);

            tooltip = new ToolTipDescriptions("scavenger", new[] { "scavenger ['clear'/'add'/'on'/'off'/'set']" },
                "N/A", "This command will control the scavenger agent.", "scavenger 'off'");
            descriptionCommands.Add("scavenger", tooltip);

            tooltip = new ToolTipDescriptions("sell", new[] { "sell" }, "N/A",
                "This command will set the Sell agent's hotbag.", "sell");
            descriptionCommands.Add("sell", tooltip);

            tooltip = new ToolTipDescriptions("setability",
                new[] { "setability ('primary'/'secondary'/'stun'/'disarm') ['on'/'off']" }, "N/A",
                "This will set a specific ability on or off. If on or off is missing, on is defaulted.",
                "setability stun");
            descriptionCommands.Add("setability", tooltip);

            tooltip = new ToolTipDescriptions("setlasttarget", new[] { "setlasttarget" }, "N/A",
                "This command will pause the script until you select a target to be set as Last Target.",
                "overhead 'set last target'\n\tsetlasttarget\n\toverhead 'set!'\n\tcast 'magic arrow'\n\twaitfortarget\n\ttarget 'last'");
            descriptionCommands.Add("setlasttarget", tooltip);

            tooltip = new ToolTipDescriptions("setvar", new[] { "setvar ('variable') ['name'] or setvariable ('variable') ['serial']" },
                "N/A",
                "If no serial is provided, this command will pause the script until you select a target to be assigned a variable.\n\t" +
                "If '!' is specified, set a temporary variable that exists only until Razor exits. If both ! and @ are specified, set a variable" +
                "only in the local scope",
                "setvar 'dummy'\n\tcast 'magic arrow'\n\twaitfortarget\n\ttarget 'dummy'");
            descriptionCommands.Add("setvar", tooltip);

            tooltip = new ToolTipDescriptions("skill", new[] { "skill 'name of skill' or skill last" }, "N/A",
                "This command will use a specific skill (assuming it's a usable skill).",
                "while mana < maxmana\n\t    say 'mediation!'\n\t    skill 'meditation'\n\t    wait 11000\n\tendwhile");
            descriptionCommands.Add("skill", tooltip);

            tooltip = new ToolTipDescriptions("sysmsg", new[] { "sysmsg ('message to display in system message')" },
                "N/A", "This command will display a message in the lower-left of the client.",
                "if stam = 100\n\t    sysmsg 'ready to go!'\n\tendif");
            descriptionCommands.Add("sysmsg", tooltip);

            tooltip = new ToolTipDescriptions("target", new[] { "target (serial) or target (x) (y) (z)" }, "N/A",
                "This command will target a specific mobile or item or target a specific location based on X/Y/Z coordinates.",
                "cast 'lightning'\n\twaitfortarget\n\ttarget 0xBB3\n\tcast 'fire field'\n\twaitfortarget\n\ttarget 5923 1145 0");
            descriptionCommands.Add("target", tooltip);

            tooltip = new ToolTipDescriptions("targettype",
                new[] { "targettype ('name') OR ('graphic') [source] [hue] [quantity] [range]" }, "N/A",
                "This command will target item or mobile by type either provided by the name or the graphic ID.\n\tYou can specify the source, color and search distance (or depth)",
                "usetype 'dagger'\n\twaitfortarget\n\ttargettype 'robe'\n\tuseobject 0x4005ECAF\n\twaitfortarget\n\ttargettype 0x1f03\n\tuseobject 0x4005ECAF\n\twaitfortarget\n\ttargettype 0x1f03 true");
            descriptionCommands.Add("targettype", tooltip);

            tooltip = new ToolTipDescriptions("targetrelloc", new[] { "targetrelloc (x-offset) (y-offset)" }, "N/A",
                "This command will target a specific location on the map relative to your position.",
                "cast 'fire field'\n\twaitfortarget\n\ttargetrelloc 1 1");
            descriptionCommands.Add("targetrelloc", tooltip);

            tooltip = new ToolTipDescriptions("undress",
                new[] { "undress ['name of dress list']' or undress 'LayerName'" }, "N/A",
                "This command will either undress you completely if no dress list is provided.\n\tIf you provide a dress list, only those specific items will be undressed. Lastly, you can define a layer name to undress.",
                "undress\n\tundress 'My Sunday Best'\n\tundress 'Shirt'\n\tundrsss 'Pants'");
            descriptionCommands.Add("undress", tooltip);

            tooltip = new ToolTipDescriptions("useonce", new[] { "useonce ['add'/'addcontainer']" }, "N/A",
                "This command will execute the UseOnce agent. If the add parameter is included, you can add items to your UseOnce list.\n\tIf the addcontainer parameter is included, you can add all items in a container to your UseOnce list.",
                "useonce\n\tuseonce 'add'\n\tuseonce 'addcontainer'");
            descriptionCommands.Add("useonce", tooltip);

            tooltip = new ToolTipDescriptions("walk", new[] { "walk ('direction')" }, "N/A",
                "This command will turn and/or walk your player in a certain direction.",
                "walk 'North'\n\twalk 'Up'\n\twalk 'West'\n\twalk 'Left'\n\twalk 'South'\n\twalk 'Down'\n\twalk 'East'\n\twalk 'Right'");
            descriptionCommands.Add("walk", tooltip);

            tooltip = new ToolTipDescriptions("wait",
                new[] { "wait [time in milliseconds or pause [time in milliseconds]" }, "N/A",
                "This command will pause the execution of a script for a given time.",
                "while stam < 100\n\t    wait 5000\n\tendwhile");
            descriptionCommands.Add("wait", tooltip);

            tooltip = new ToolTipDescriptions("pause",
                new[] { "pause [time in milliseconds or pause [time in milliseconds]" }, "N/A",
                "This command will pause the execution of a script for a given time.",
                "while stam < 100\n\t    wait 5000\n\tendwhile");
            descriptionCommands.Add("pause", tooltip);

            tooltip = new ToolTipDescriptions("waitforgump", new[] { "waitforgump [gump id]" }, "N/A",
                "This command will wait for a gump. If no gump id is provided, it will wait for **any * *gump.",
                "waitforgump\n\twaitforgump 4");
            descriptionCommands.Add("waitforgump", tooltip);

            tooltip = new ToolTipDescriptions("waitformenu", new[] { "waitformenu [menu id]" }, "N/A",
                "This command will wait for menu (not a context menu). If no menu id is provided, it will wait for **any * *menu.",
                "waitformenu\n\twaitformenu 4");
            descriptionCommands.Add("waitformenu", tooltip);

            tooltip = new ToolTipDescriptions("waitforprompt", new[] { "waitforprompt" }, "N/A",
                "This command will wait for a prompt before continuing.",
                "dclicktype 'rune'\n\twaitforprompt\n\tpromptresponse 'to home'");
            descriptionCommands.Add("waitforprompt", tooltip);

            tooltip = new ToolTipDescriptions("waitfortarget",
                new[] { "waitfortarget [pause in milliseconds] or wft [pause in milliseconds]" }, "N/A",
                "This command will cause the script to pause until you have a target cursor.\n\tBy default it will wait 30 seconds but you can define a specific wait time if you prefer.",
                "cast 'energy bolt'\n\twaitfortarget\n\thotkey 'Target Closest Enemy'");
            descriptionCommands.Add("waitfortarget", tooltip);

            tooltip = new ToolTipDescriptions("clearsysmsg",
                new[] { "clearsysmsg" }, "N/A",
                "This command will clear the internal system message queue used with insysmsg.",
                "clearsysmsg\n");
            descriptionCommands.Add("clearsysmsg", tooltip);

            tooltip = new ToolTipDescriptions("clearjournal",
                new[] { "clearjournal" }, "N/A",
                "This command (same as clearjournal) will clear the internal system message queue used with insysmsg.",
                "clearjournal\n");
            descriptionCommands.Add("clearjournal", tooltip);

            tooltip = new ToolTipDescriptions("waitforsysmsg",
                new[] { "waitforsysmsg" }, "N/A",
                "This command will pause the script until the message defined is in the system message queue.",
                "waitforsysmsg 'message here'\n");
            descriptionCommands.Add("waitforsysmsg", tooltip);

            tooltip = new ToolTipDescriptions("random",
                new[] { "random [max number]" }, "N/A",
                "This command output a random number between 1 and the max number provided.",
                "random '15'\n");
            descriptionCommands.Add("random", tooltip);

            #endregion

            #region Outlands

            tooltip = new ToolTipDescriptions("warmode",
                new[] { "warmode ('on' / 'off')" }, "N/A",
                "Set your current war mode to on or off",
                "warmode on\n");
            descriptionCommands.Add("warmode", tooltip);

            tooltip = new ToolTipDescriptions("getlabel",
                new[] { "getlabel ('serial') ('name')" }, "N/A",
                "Retrieve the label for the item with the given serial and store it in name. A label is the text displayed when single clicking.",
                "getlabel 0x1234 mylabel\n");
            descriptionCommands.Add("getlabel", tooltip);

            tooltip = new ToolTipDescriptions("createlist",
                new[] { "createlist ('list name')" }, "N/A",
                "Create a new list",
                "createlist mylist\n");
            descriptionCommands.Add("createlist", tooltip);

            tooltip = new ToolTipDescriptions("clearlist",
                new[] { "clearlist ('list name')" }, "N/A",
                "Clear an existing list. The list still exists after this operation.",
                "clearlist mylist\n");
            descriptionCommands.Add("clearlist", tooltip);

            tooltip = new ToolTipDescriptions("removelist",
                new[] { "removelist ('list name')" }, "N/A",
                "Remove a list",
                "removelist mylist\n");
            descriptionCommands.Add("removelist", tooltip);

            tooltip = new ToolTipDescriptions("pushlist",
                new[] { "pushlist ('list name') ('element value') ['front'/'back']" }, "N/A",
                "Push an element to a list",
                "pushlist mylist myvalue front\n");
            descriptionCommands.Add("pushlist", tooltip);

            tooltip = new ToolTipDescriptions("listexists",
                new[] { "listexists ('list name')" }, "True if list specified by name exists",
                "Check if list specified by list_name exist",
                "if listexists myList\n\tpoplist mylist front\nendif\n");
            descriptionCommands.Add("listexists", tooltip);

            tooltip = new ToolTipDescriptions("poplist",
                new[] { "poplist ('list name') ('element value'/'front'/'back')" }, "N/A",
                "Pop an element from the list",
                "poplist mylist front\n");
            descriptionCommands.Add("poplist", tooltip);

            tooltip = new ToolTipDescriptions("createtimer",
                new[] { "createtimer ('timer name')" }, "N/A",
                "Create and start a new timer",
                "creatertimer atimer\n");
            descriptionCommands.Add("createtimer", tooltip);

            tooltip = new ToolTipDescriptions("removetimer",
                new[] { "removetimer ('timer name')" }, "N/A",
                "Stop and remove a timer",
                "removetimer atimer\n");
            descriptionCommands.Add("removetimer", tooltip);

            tooltip = new ToolTipDescriptions("settimer",
                new[] { "settimer ('timer name') ('value')" }, "N/A",
                "Set a timer to the given time value",
                "settimer atimer 10\n");
            descriptionCommands.Add("settimer", tooltip);

            tooltip = new ToolTipDescriptions("timerexists",
                new[] { "timerexists ('timer name')" }, "Return true is target specified by name exists",
                "Check if target with specified name exists",
                "if timerexists 'clock'\n\tcreatetimer 'clock'\nendif\n");
            descriptionCommands.Add("timerexists", tooltip);

            tooltip = new ToolTipDescriptions("timer",
                new[] { "timer ('timer name')" }, "Return value of timer specified by name",
                "Get value of timer specified by name",
                "if timer 'discoBuff' > 5000\n\tskill 'Discorance'\n\twft\n\ttarget 'spellBookSerial'\nendif\n");
            descriptionCommands.Add("timer", tooltip);

            tooltip = new ToolTipDescriptions("unsetvar",
                new[] { "unsetvar ('name')" }, "N/A",
                "Unset a variable. If '!' is specified, unset a temporary variable. If both ! and @ are specified, unset a temporary local variable.",
                "unsetvar myvar\n");
            descriptionCommands.Add("unsetvar", tooltip);

            tooltip = new ToolTipDescriptions("rename",
                new[] { "rename ('serial') ('name')" }, "N/A",
                "Rename a creature (one of your followers) with the provided name",
                "rename mypet fido\n");
            descriptionCommands.Add("rename", tooltip);

            tooltip = new ToolTipDescriptions("gumpexists",
                new[] { "gumpexists ('gumpId or any')" }, "True if gump exists",
                "Check if gump with specified id or any exist",
                "gumpexists 0x41434\n");
            descriptionCommands.Add("gumpexists", tooltip);

            tooltip = new ToolTipDescriptions("ingump",
                new[] { "ingump ('text') [gumpId/any]" }, "True if text exists in gumps",
                "Check if text in gump with specified id or any exists",
                "ingump test any\n");
            descriptionCommands.Add("ingump", tooltip);

            tooltip = new ToolTipDescriptions("dead",
                new[] { "dead ['serial']" }, "True mobile with serial is dead",
                "Check if mobile (default 'self') is dead",
                "if dead 0x43434\n\tcast 'Resurrection'\nendif\n");
            descriptionCommands.Add("dead", tooltip);

            tooltip = new ToolTipDescriptions("noto",
                new[] { "noto ('serial')" }, "Name of the notority flag of mobile",
                "Return Notority name of mobile by serial",
                "if noto 0x43434 = 'innocent'\n\tcast 'Heal'\nendif\n");
            descriptionCommands.Add("noto", tooltip);

            tooltip = new ToolTipDescriptions("invul",
                new[] { "invul" }, "True if player is invaluable",
                "Check if player is invaluable",
                "if invul\n\toverhead 'WoW'\nendif\n");
            descriptionCommands.Add("invul", tooltip);

            tooltip = new ToolTipDescriptions("paralyzed",
                new[] { "paralyzed" }, "True if player is paralyzed",
                "Check if player is paralyzed",
                "if paralyzed\n\tsay [pouch\nendif\n");
            descriptionCommands.Add("paralyzed", tooltip);

            tooltip = new ToolTipDescriptions("counttype",
                new[] { "counttype (name or graphic) [src] [hue] [range]" }, "Number of items in container",
                "Get number of items by name or graphic.\n\tYou can specify the source, color and search distance (or depth)",
                "if counttype 'dagger' 'ground'\n\torganizer 1\nendif\n");
            descriptionCommands.Add("counttype", tooltip);

            tooltip = new ToolTipDescriptions("diffmana",
                new[] { "diffmana" }, "Absolute value between max mana and current mana",
                "Get absolute difference between max mana and mana",
                "if diffmana > 20\n\tskill Meditiation\nendif\n");
            descriptionCommands.Add("diffmana", tooltip);

            tooltip = new ToolTipDescriptions("diffstam",
                new[] { "diffstam" }, "Absolute value between max stamina and current stamina",
                "Get absolute difference between max stamina and stamina",
                "if diffstam > 20\n\toverhead 'I need a rest'\nendif\n");
            descriptionCommands.Add("diffstam", tooltip);

            tooltip = new ToolTipDescriptions("diffhits",
                new[] { "diffhits" }, "Absolute value between max hp and current hp",
                "Get absolute difference between max hp and hp",
                "if diffhits > 20\n\tcast 'Heal'\n\twft\n\ttarget 'self'\nendif\n");
            descriptionCommands.Add("diffhits", tooltip);

            tooltip = new ToolTipDescriptions("diffweight",
                new[] { "diffweight" }, "Absolute value between weight hp and current weight",
                "Get absolute difference between max weight and weight",
                "if diffweight > 20\n\tcast 'Bless'\n\twft\n\ttarget 'self'\nendif\n");
            descriptionCommands.Add("diffweight", tooltip);

            tooltip = new ToolTipDescriptions("maxweight",
                new[] { "maxweight" }, "Return player current max weight",
                "Get player current max weight",
                "if maxweight < 500\n\tcast 'Bless'\n\twft\n\ttarget 'self'\nendif\n");
            descriptionCommands.Add("maxweight", tooltip);

            tooltip = new ToolTipDescriptions("targetexists",
                new[] { "targetexists" }, "Return player current max weight",
                "Get player current max weight",
                "if targetexists < 500\n\tcast 'Bless'\n\twft\n\ttarget 'self'\nendif\n");
            descriptionCommands.Add("targetexists", tooltip);

            tooltip = new ToolTipDescriptions("name",
                new[] { "name" }, "Return player name",
                "Get player name",
                "if name = 'Bob'\n\toverhead 'Good... Its me'\nendif\n");
            descriptionCommands.Add("name", tooltip);

            tooltip = new ToolTipDescriptions("hue",
                new[] { "hue (serial)" }, "Return hue of item",
                "Get hue of item specified by serial.",
                "if hue 'boardSerial' = 1763\n\toverhead 'Yeah, averboard!'\nendif\n");
            descriptionCommands.Add("hue", tooltip);

            tooltip = new ToolTipDescriptions("followers",
                new[] { "followers" }, "Return the number of followers",
                "Get the number of followers for player",
                "if followers > 0\n\tsay 'all release'\nendif\n");
            descriptionCommands.Add("followers", tooltip);

            tooltip = new ToolTipDescriptions("clearignore",
                new[] { "clearignore" }, "N/A",
                "Clear list of ignored serials",
                "clearignore\n");
            descriptionCommands.Add("clearignore", tooltip);

            tooltip = new ToolTipDescriptions("ignore",
                new[] { "ignore (serial)" }, "N/A",
                "Add mobile or item serial to ignore list",
                "if targettype 'an eagle' as mob\n\tskill 'Animal Lore'\n\twft\n\ttarget mob\n\tignore mob\nendif\n");
            descriptionCommands.Add("ignore", tooltip);

            tooltip = new ToolTipDescriptions("setskill",
                new[] { "setskill (skill_name) (up/donw/lock)" }, "N/A",
                "Set state of skill specified by skill_name to state specified by second argument\n\tUse the name of skill visible in Skills tab from Paperdoll",
                "setskill 'Blacksmithy' 'lock'\n");
            descriptionCommands.Add("setskill", tooltip);

            #endregion

            if (!Config.GetBool("DisableScriptTooltips"))
            {
                List<AutocompleteItem> items = new List<AutocompleteItem>();

                foreach (var item in keywords)
                {
                    items.Add(new AutocompleteItem(item));
                }

                foreach (var item in commands)
                {
                    descriptionCommands.TryGetValue(item, out ToolTipDescriptions element);

                    if (element != null)
                    {
                        items.Add(new MethodAutocompleteItemAdvance(item)
                        {
                            ImageIndex = 2,
                            ToolTipTitle = element.Title,
                            ToolTipText = element.ToolTipDescription()
                        });
                    }
                    else
                    {
                        items.Add(new MethodAutocompleteItemAdvance(item)
                        {
                            ImageIndex = 2
                        });
                    }
                }

                _autoCompleteMenu.Items.SetAutocompleteItems(items);
                _autoCompleteMenu.Items.MaximumSize =
                    new Size(_autoCompleteMenu.Items.Width + 20, _autoCompleteMenu.Items.Height);
                _autoCompleteMenu.Items.Width = _autoCompleteMenu.Items.Width + 20;
            }
            else
            {
                _autoCompleteMenu.Items.SetAutocompleteItems(new List<AutocompleteItem>());
            }

            ScriptEditor.Language = FastColoredTextBoxNS.Language.Razor;
        }

        public class ToolTipDescriptions
        {
            public string Title;
            public string[] Parameters;
            public string Returns;
            public string Description;
            public string Example;

            public ToolTipDescriptions(string title, string[] parameter, string returns, string description,
                string example)
            {
                Title = title;
                Parameters = parameter;
                Returns = returns;
                Description = description;
                Example = example;
            }

            public string ToolTipDescription()
            {
                string complete_description = string.Empty;

                complete_description += "Parameter(s): ";

                foreach (string parameter in Parameters)
                    complete_description += "\n\t" + parameter;

                complete_description += "\nDescription:";

                complete_description += "\n\t" + Description;

                complete_description += "\nExample(s):";

                complete_description += "\n\t" + Example;

                return complete_description;
            }
        }

        public class MethodAutocompleteItemAdvance : MethodAutocompleteItem
        {
            string firstPart;
            string lastPart;

            public MethodAutocompleteItemAdvance(string text)
                : base(text)
            {
                var i = text.LastIndexOf(' ');
                if (i < 0)
                    firstPart = text;
                else
                {
                    firstPart = text.Substring(0, i);
                    lastPart = text.Substring(i + 1);
                }
            }

            public override CompareResult Compare(string fragmentText)
            {
                int i = fragmentText.LastIndexOf(' ');

                if (i < 0)
                {
                    if (firstPart.StartsWith(fragmentText) && string.IsNullOrEmpty(lastPart))
                        return CompareResult.VisibleAndSelected;
                    //if (firstPart.ToLower().Contains(fragmentText.ToLower()))
                    //  return CompareResult.Visible;
                }
                else
                {
                    var fragmentFirstPart = fragmentText.Substring(0, i);
                    var fragmentLastPart = fragmentText.Substring(i + 1);


                    if (firstPart != fragmentFirstPart)
                        return CompareResult.Hidden;

                    if (lastPart != null && lastPart.StartsWith(fragmentLastPart))
                        return CompareResult.VisibleAndSelected;

                    if (lastPart != null && lastPart.ToLower().Contains(fragmentLastPart.ToLower()))
                        return CompareResult.Visible;
                }

                return CompareResult.Hidden;
            }

            public override string GetTextForReplace()
            {
                if (lastPart == null)
                    return firstPart;

                return firstPart + " " + lastPart;
            }

            public override string ToString()
            {
                if (lastPart == null)
                    return firstPart;

                return lastPart;
            }
        }

        public static void RedrawScripts()
        {
            ScriptTree.SafeAction(s =>
            {
                s.BeginUpdate();
                s.Nodes.Clear();
                Recurse(s.Nodes, Config.GetUserDirectory("Scripts"));
                s.EndUpdate();
                s.Refresh();
                s.Update();
            });

            RedrawScriptVariables();
        }

        public static TreeNode GetScriptDirNode()
        {
            if (ScriptTree.SelectedNode == null)
            {
                return null;
            }

            if (ScriptTree.SelectedNode.Tag is string)
                return ScriptTree.SelectedNode;
                
            if (!(ScriptTree.SelectedNode.Parent?.Tag is string))
                return null;
                
            return ScriptTree.SelectedNode.Parent;
        }

        public static void AddScriptNode(TreeNode node)
        {
            if (node == null)
            {
                ScriptTree.Nodes.Add(node);
            }
            else
            {
                node.Nodes.Add(node);
            }

            ScriptTree.SelectedNode = node;
        }

        private static void Recurse(TreeNodeCollection nodes, string path)
        {
            try
            {
                var razorFiles = Directory.GetFiles(path, "*.razor");
                razorFiles = razorFiles.OrderBy(fileName => fileName).ToArray();

                foreach (var file in razorFiles)
                {
                    RazorScript script = null;

                    foreach (RazorScript razorScript in _scriptList)
                    {
                        if (razorScript.Path.Equals(file))
                        {
                            script = razorScript;
                        }
                    }

                    if (script == null)
                    {
                        script = AddScript(file);
                    }

                    if (nodes != null)
                    {
                        TreeNode node = new TreeNode(script.Name)
                        {
                            Tag = script
                        };

                        nodes.Add(node);
                    }
                }
            }
            catch
            {
                // ignored
            }

            try
            {

                foreach (string directory in Directory.GetDirectories(path))
                {
                    if (!string.IsNullOrEmpty(directory) && !directory.Equals(".") && !directory.Equals(".."))
                    {
                        if (nodes != null)
                        {
                            TreeNode node = new TreeNode($"[{Path.GetFileName(directory)}]")
                            {
                                Tag = directory
                            };

                            nodes.Add(node);

                            Recurse(node.Nodes, directory);
                        }
                        else
                        {
                            Recurse(null, directory);
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }
        }

        public static void GetGumpInfo(string[] param)
        {
            Targeting.OneTimeTarget(OnGetItemInfoTarget);
            Client.Instance.SendToClient(new UnicodeMessage(0xFFFFFFFF, -1, MessageType.Regular, 0x3B2, 3,
                Language.CliLocName, "System", "Select an item or mobile to view/inspect"));
        }

        private static void OnGetItemInfoTarget(bool ground, Serial serial, Point3D pt, ushort gfx)
        {
            Item item = World.FindItem(serial);

            if (item == null)
            {
                Mobile mobile = World.FindMobile(serial);

                if (mobile == null)
                    return;

                MobileInfoGump gump = new MobileInfoGump(mobile);
                gump.SendGump();
            }
            else
            {
                ItemInfoGump gump = new ItemInfoGump(item);
                gump.SendGump();
            }
        }
    }
}