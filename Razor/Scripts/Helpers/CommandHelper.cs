using System;
using System.Collections.Generic;
using System.Linq;
using Assistant.Scripts.Engine;

namespace Assistant.Scripts.Helpers
{
    public static class CommandHelper
    {
        private static IEnumerable<Item> FilterItems(IEnumerable<Item> items, int hue, short qt, Serial src, int range)
        {
            foreach (var item in items)
            {
                if (hue != -1 && item.Hue != hue)
                {
                    continue;
                }
                // For items that are not stackable Amount is equal 0
                if (qt > 1 && item.Amount < qt)
                {
                    continue;
                }

                if (src != 0)
                {
                    switch (item.Container)
                    {
                        case Item i:
                            if (range == -1)
                                range = 3;
                            if (!LookRec(i, src, range))
                                continue;
                            break;
                        case Mobile m:
                            if (m.Serial != src)
                                continue;
                            break;
                        case Serial s:
                            if (s.Value != src)
                                continue;
                            break;
                        default:
                            continue;
                    }
                }
                else if (item.Container != null)
                {
                    continue;
                }

                if (range > 0 && src <= 0 && !Utility.InRange(World.Player.Position, item.Position, range))
                {
                    continue;
                }

                if (Interpreter.CheckIgnored(item.Serial))
                    continue;

                yield return item;
            }
        }

        /// <summary>
        /// Looking recuirse in container
        /// - First Container of item is searching serial = return true
        /// - If not, take container of item container and look again
        /// - If item container is Mobile, that mean we are on a top of the chain and we should look for serial
        /// (set var will set serial of follower not their backpack)
        /// </summary>
        /// <param name="item">First level of backpack</param>
        /// <param name="serial">Serial of container that we are looking for</param>
        /// <param name="deep">how deep</param>
        /// <returns></returns>
        private static bool LookRec(Item item, Serial serial, int deep)
        {
            var sItem = item;
            for (var i = 0; i < deep; i++)
            {
                // Check for trapped pouch
                if (sItem == null || sItem.Hue == 38 && sItem.ItemID.Value == 3705)
                    return false;

                if (sItem.Serial == serial)
                    return true;
                switch (sItem.Container)
                {
                    case Item itm:
                        sItem = itm;
                        break;
                    // Mob is end of chain on packet llamas and players
                    case Mobile mob:
                        return mob.Serial == serial;
                    default:
                        sItem = null;
                        break;
                }
            }
            return false;
        }

        /// <summary>
        /// Common logic for dclicktype and targettype to find items by name
        /// </summary>
        /// <param name="name"></param>
        /// <param name="hue">Hue number</param>
        /// <param name="src">Source name</param>
        /// <param name="qt">Quantity</param>
        /// <param name="range">Range</param>
        /// <returns></returns>
        public static List<Item> GetItemsByName(string name, int hue, Serial src, short qt, int range)
        {
            return FilterItems(World.FindItemsByName(name), hue, qt, src, range).ToList();
        }

        /// <summary>
        /// Common logic for dclicktype and targettype to find items by id
        /// </summary>
        /// <param name="id"></param>
        /// <param name="hue">Hue number</param>
        /// <param name="src">Source name</param>
        /// <param name="qt">Quantity</param>
        /// <param name="range">Range</param>
        /// <returns></returns>
        public static List<Item> GetItemsById(ushort id, int hue, Serial src, short qt, int range)
        {
            return FilterItems(World.FindItemsById(id), hue, qt, src, range).ToList();
        }

        /// <summary>
        /// Common logic for dclicktype and targettype to find mobiles by name
        /// </summary>
        /// <param name="name"></param>
        /// <param name="range">Range</param>
        /// <returns></returns>
        public static List<Mobile> GetMobilesByName(string name, int range)
        {
            List<Mobile> mobiles = new List<Mobile>();

            foreach (var m in World.FindMobilesByName(name))
            {
                if (m.IsGhost || m.IsHuman)
                {
                    continue;
                }

                if (range != -1 && !Utility.InRange(World.Player.Position, m.Position, range))
                {
                    continue;
                }

                mobiles.Add(m);
            }

            return mobiles;
        }

        /// <summary>
        /// Common logic for dclicktype and targettype to find mobiles by id
        /// </summary>
        /// <param name="id"></param>
        /// <param name="range">Range</param>
        /// <returns></returns>
        public static List<Mobile> GetMobilesById(ushort id, int range)
        {
            List<Mobile> mobiles = new List<Mobile>();

            if (range == -1)
            {
                range = 18;
            }

            foreach (var m in World.MobilesInRange(range))
            {
                if (m.IsGhost || m.IsHuman)
                {
                    continue;
                }

                if (m.Body != id)
                {
                    continue;
                }

                mobiles.Add(m);
            }

            return mobiles;
        }

        /// <summary>
        /// Check if passed string is number and assign out variable to that number
        /// </summary>
        /// <param name="sNumber">String with number</param>
        public static int IsNumberOrAny(string sNumber)
        {
            var num = Utility.ToInt32(sNumber, -2);
            if (num != -2)
            {
                return num;
            }

            if (sNumber.ToLower() != "any")
            {
                throw new RunTimeError("Wrong parameter");
            }

            return -1;
        }

        /// <summary>
        /// Deconstruct arguments
        /// </summary>
        /// <param name="args">Array with arguments</param>
        public static (Serial, int, int, int) ParseFindArguments(Variable[] args)
        {
            int[] result = { -1, -1, -1 };

            Serial src = args.Length > 1 ? args[1].AsSerial() : World.Player.Backpack.Serial.Value;

            // Hue
            if (args.Length > 2)
            {
                result[0] = IsNumberOrAny(args[2].AsString());
            }

            // Qty
            if (args.Length > 3)
            {
                result[1] = IsNumberOrAny(args[3].AsString());
            }

            // Range
            if (args.Length > 4)
            {
                result[2] = IsNumberOrAny(args[4].AsString());
            }

            return (src, result[0], result[1], result[2]);
        }

        public static void SendWarning(string command, string message, bool quiet)
        {
            if (!quiet)
            {
                World.Player.SendMessage(MsgLevel.Warning, $"{command} - {message}");
            }
        }

        public static void SendMessage(string message, bool quiet)
        {
            if (!quiet)
            {
                World.Player.SendMessage(MsgLevel.Force, message);
            }
        }

        /// <summary>
        /// Parse the script input to target the correct mobile
        /// </summary>
        /// <param name="args"></param>
        /// <param name="closest"></param>
        /// <param name="random"></param>
        /// <param name="next"></param>
        /// <param name="prev"></param>
        public static void FindTarget(Variable[] args, bool closest, bool random = false, bool next = false, bool prev = false)
        {
            ScriptManager.TargetFound = false;

            // Do a basic t
            if (args.Length == 1)
            {
                if (closest)
                {
                    Targeting.TargetClosest();
                }
                else if (random)
                {
                    Targeting.TargetRandAnyone();
                }
                else if (next)
                {
                    Targeting.NextTarget();
                }
                else
                {
                    Targeting.PrevTarget();
                }
            }
            else if ((next || prev) && args.Length == 2)
            {
                switch (args[1].AsString())
                {
                    case "human":
                    case "humanoid":

                        if (next)
                        {
                            Targeting.NextTargetHumanoid();
                        }
                        else
                        {
                            Targeting.PrevTargetHumanoid();
                        }

                        break;
                    case "monster":
                        if (next)
                        {
                            Targeting.NextTargetMonster();
                        }
                        else
                        {
                            Targeting.PrevTargetMonster();
                        }

                        break;
                    case "friend":
                        if (next)
                        {
                            Targeting.NextTargetFriend();
                        }
                        else
                        {
                            Targeting.PrevTargetFriend();
                        }

                        break;
                    case "nonfriendly":
                        if (next)
                        {
                            Targeting.NextTargetNonFriend();
                        }
                        else
                        {
                            Targeting.PrevTargetNonFriend();
                        }

                        break;
                    default:
                        throw new RunTimeError(
                            $"Unknown target type: '{args[1].AsString()}' - Missing type? (human/monster)");
                }
            }
            else if (args.Length > 1)
            {
                string list = args[1].AsString();

                if (list.IndexOf('!') != -1)
                {
                    FindTargetPriority(args, closest, random, next);
                }
                else if (list.IndexOf(',') != -1)
                {
                    FindTargetNotoriety(args, closest, random, next);
                }
                else
                {
                    FindTargetPriority(args, closest, random, next);
                }
            }
        }

        /// <summary>
        /// Find targets based on notoriety
        /// </summary>
        /// <param name="args"></param>
        /// <param name="closest"></param>
        /// <param name="random"></param>
        /// <param name="next"></param>
        private static void FindTargetNotoriety(Variable[] args, bool closest, bool random, bool next)
        {
            string[] notoList = args[1].AsString().Split(',');

            List<int> notoTypes = new List<int>();

            foreach (string noto in notoList)
            {
                Targeting.TargetType type = (Targeting.TargetType)Enum.Parse(typeof(Targeting.TargetType), noto, true);

                /*NonFriendly, //Attackable, Criminal, Enemy, Murderer
                Friendly, //Innocent, Guild/Ally 
                Red, //Murderer
                Blue, //Innocent
                Gray, //Attackable, Criminal
                Grey, //Attackable, Criminal
                Green, //GuildAlly
                Guild, //GuildAlly*/

                switch (type)
                {
                    case Targeting.TargetType.Friendly:
                        notoTypes.Add((int) Targeting.TargetType.Innocent);
                        notoTypes.Add((int) Targeting.TargetType.GuildAlly);
                        break;
                    case Targeting.TargetType.NonFriendly:
                        notoTypes.Add((int)Targeting.TargetType.Attackable);
                        notoTypes.Add((int)Targeting.TargetType.Criminal);
                        notoTypes.Add((int)Targeting.TargetType.Enemy);
                        notoTypes.Add((int)Targeting.TargetType.Murderer);
                        break;
                    case Targeting.TargetType.Red:
                        notoTypes.Add((int)Targeting.TargetType.Murderer);
                        break;
                    case Targeting.TargetType.Blue:
                        notoTypes.Add((int)Targeting.TargetType.Innocent);
                        break;
                    case Targeting.TargetType.Gray:
                    case Targeting.TargetType.Grey:
                        notoTypes.Add((int)Targeting.TargetType.Attackable);
                        notoTypes.Add((int)Targeting.TargetType.Criminal);
                        break;
                    case Targeting.TargetType.Green:
                    case Targeting.TargetType.Guild:
                        notoTypes.Add((int)Targeting.TargetType.GuildAlly);
                        break;
                    default:
                        notoTypes.Add((int)type);
                        break;
                }
            }

            if (args.Length == 3)
            {
                if (args[2].AsString().IndexOf("human", StringComparison.InvariantCultureIgnoreCase) != -1)
                {
                    if (closest)
                    {
                        Targeting.ClosestHumanoidTarget(notoTypes.ToArray());
                    }
                    else if (random)
                    {
                        Targeting.RandomHumanoidTarget(notoTypes.ToArray());
                    } 
                    else if (next)
                    {
                        Targeting.NextPrevTargetNotorietyHumanoid(true, notoTypes.ToArray());
                    }
                    else
                    {
                        Targeting.NextPrevTargetNotorietyHumanoid(false, notoTypes.ToArray());
                    }
                }
                else if (args[2].AsString().IndexOf("monster", StringComparison.InvariantCultureIgnoreCase) != -1)
                {
                    if (closest)
                    {
                        Targeting.ClosestMonsterTarget(notoTypes.ToArray());
                    }
                    else if (random)
                    {
                        Targeting.RandomMonsterTarget(notoTypes.ToArray());
                    }
                    else if (next)
                    {
                        Targeting.NextPrevTargetNotorietyMonster(true, notoTypes.ToArray());
                    }
                    else
                    {
                        Targeting.NextPrevTargetNotorietyMonster(false, notoTypes.ToArray());
                    }
                }
            }
            else
            {
                if (closest)
                {
                    Targeting.ClosestTarget(notoTypes.ToArray());
                }
                else if (random)
                {
                    Targeting.RandomTarget(notoTypes.ToArray());
                }
                else if (next)
                {
                    Targeting.NextPrevTargetNotoriety(true, notoTypes.ToArray());
                }
                else
                {
                    Targeting.NextPrevTargetNotoriety(false, notoTypes.ToArray());
                }
            }
        }

        /// <summary>
        /// Find a target based on a priority list of notorieties 
        /// </summary>
        /// <param name="args"></param>
        /// <param name="closest"></param>
        /// <param name="random"></param>
        /// <param name="next"></param>
        private static void FindTargetPriority(Variable[] args, bool closest, bool random, bool next)
        {
            string[] notoList = args[1].AsString().Split('!');

            foreach (string noto in notoList)
            {
                if (ScriptManager.TargetFound)
                {
                    break;
                }

                switch (noto)
                {
                    case "enemy":
                        if (args.Length == 3)
                        {
                            if (args[2].AsString().IndexOf("human", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseEnemyHumanoid();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandEnemyHumanoid();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetEnemyHumanoid();
                                }
                                else
                                {
                                    Targeting.PrevTargetEnemyHumanoid();
                                }
                            }
                            else if (args[2].AsString()
                                .IndexOf("monster", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseEnemyMonster();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandEnemyMonster();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetEnemyMonster();
                                }
                                else
                                {
                                    Targeting.PrevTargetEnemyMonster();
                                }
                            }
                        }
                        else
                        {
                            if (closest)
                            {
                                Targeting.TargetCloseEnemy();
                            }
                            else if (random)
                            {
                                Targeting.TargetRandEnemy();
                            }
                        }

                        break;
                    case "friend":
                        if (closest)
                        {
                            Targeting.TargetClosestFriend();
                        }
                        else if (random)
                        {
                            Targeting.TargetRandFriend();
                        }
                        else if (next)
                        {
                            Targeting.NextTargetFriend();
                        }
                        else
                        {
                            Targeting.PrevTargetFriend();
                        }

                        break;
                    case "friendly":
                        if (args.Length == 3)
                        {
                            if (args[2].AsString().IndexOf("human", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseFriendlyHumanoid();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandFriendlyHumanoid();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetFriendlyHumanoid();
                                }
                                else
                                {
                                    Targeting.PrevTargetFriendlyHumanoid();
                                }
                            }
                            else if (args[2].AsString()
                                .IndexOf("monster", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseFriendlyMonster();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandFriendlyMonster();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetFriendlyMonster();
                                }
                                else
                                {
                                    Targeting.PrevTargetFriendlyMonster();
                                }
                            }
                        }
                        else
                        {
                            if (closest)
                            {
                                Targeting.TargetCloseFriendly();
                            }
                            else if (random)
                            {
                                Targeting.TargetRandFriendly();
                            }
                        }

                        break;
                    case "gray":
                    case "grey":
                        if (args.Length == 3)
                        {
                            if (args[2].AsString().IndexOf("human", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseGreyHumanoid();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandGreyHumanoid();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetGreyHumanoid();
                                }
                                else
                                {
                                    Targeting.PrevTargetGreyHumanoid();
                                }
                            }
                            else if (args[2].AsString()
                                .IndexOf("monster", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseGreyMonster();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandGreyMonster();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetGreyMonster();
                                }
                                else
                                {
                                    Targeting.PrevTargetGreyMonster();
                                }
                            }
                        }
                        else
                        {
                            if (closest)
                            {
                                Targeting.TargetCloseGrey();
                            }
                            else if (random)
                            {
                                Targeting.TargetRandGrey();
                            }
                        }

                        break;
                    case "criminal":
                        if (args.Length == 3)
                        {
                            if (args[2].AsString().IndexOf("human", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseCriminalHumanoid();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandCriminalHumanoid();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetCriminalHumanoid();
                                }
                                else
                                {
                                    Targeting.PrevTargetCriminalHumanoid();
                                }
                            }
                            else if (args[2].AsString()
                                .IndexOf("monster", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseCriminalMonster();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandCriminalMonster();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetCriminalMonster();
                                }
                                else
                                {
                                    Targeting.PrevTargetCriminalMonster();
                                }
                            }
                        }
                        else
                        {
                            if (closest)
                            {
                                Targeting.TargetCloseCriminal();
                            }
                            else if (random)
                            {
                                Targeting.TargetRandCriminal();
                            }
                        }

                        break;
                    case "blue":
                    case "innocent":
                        if (args.Length == 3)
                        {
                            if (args[2].AsString().IndexOf("human", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseInnocentHumanoid();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandInnocentHumanoid();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetInnocentHumanoid();
                                }
                                else
                                {
                                    Targeting.PrevTargetInnocentHumanoid();
                                }
                            }
                            else if (args[2].AsString()
                                .IndexOf("monster", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseInnocentMonster();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandInnocentMonster();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetInnocentMonster();
                                }
                                else
                                {
                                    Targeting.PrevTargetInnocentMonster();
                                }
                            }
                        }
                        else
                        {
                            if (closest)
                            {
                                Targeting.TargetCloseInnocent();
                            }
                            else if (random)
                            {
                                Targeting.TargetRandInnocent();
                            }
                        }

                        break;
                    case "red":
                    case "murderer":
                        if (args.Length == 3)
                        {
                            if (args[2].AsString().IndexOf("human", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseRedHumanoid();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandRedHumanoid();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetMurdererHumanoid();
                                }
                                else
                                {
                                    Targeting.PrevTargetMurdererHumanoid();
                                }
                            }
                            else if (args[2].AsString()
                                .IndexOf("monster", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseRedMonster();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandRedMonster();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetMurdererMonster();
                                }
                                else
                                {
                                    Targeting.PrevTargetMurdererMonster();
                                }
                            }
                        }
                        else
                        {
                            if (closest)
                            {
                                Targeting.TargetCloseRed();
                            }
                            else if (random)
                            {
                                Targeting.TargetRandRed();
                            }
                        }

                        break;
                    case "nonfriendly":
                        if (args.Length == 3)
                        {
                            if (args[2].AsString().IndexOf("human", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseNonFriendlyHumanoid();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandNonFriendlyHumanoid();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetNonFriendlyHumanoid();
                                }
                                else
                                {
                                    Targeting.PrevTargetNonFriendlyHumanoid();
                                }
                            }
                            else if (args[2].AsString()
                                .IndexOf("monster", StringComparison.InvariantCultureIgnoreCase) != -1)
                            {
                                if (closest)
                                {
                                    Targeting.TargetCloseNonFriendlyMonster();
                                }
                                else if (random)
                                {
                                    Targeting.TargetRandNonFriendlyMonster();
                                }
                                else if (next)
                                {
                                    Targeting.NextTargetNonFriendlyMonster();
                                }
                                else
                                {
                                    Targeting.PrevTargetNonFriendlyMonster();
                                }
                            }
                        }
                        else
                        {
                            if (closest)
                            {
                                Targeting.TargetCloseNonFriendly();
                            }
                            else if (random)
                            {
                                Targeting.TargetRandNonFriendly();
                            }
                        }

                        break;
                    default:
                        throw new RunTimeError($"Unknown target type: '{args[1].AsString()}'");
                }
            }
        }
    }
}
