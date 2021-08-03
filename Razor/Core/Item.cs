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
using System.IO;
using System.Collections.Generic;
using Assistant.Agents;
using Assistant.Core;

namespace Assistant
{
    public enum Layer : byte
    {
        Invalid = 0x00,

        FirstValid = 0x01,

        RightHand = 0x01,
        LeftHand = 0x02,
        Shoes = 0x03,
        Pants = 0x04,
        Shirt = 0x05,
        Head = 0x06,
        Gloves = 0x07,
        Ring = 0x08,
        Talisman = 0x09,
        Neck = 0x0A,
        Hair = 0x0B,
        Waist = 0x0C,
        InnerTorso = 0x0D,
        Bracelet = 0x0E,
        Face = 0x0F,
        FacialHair = 0x10,
        MiddleTorso = 0x11,
        Earrings = 0x12,
        Arms = 0x13,
        Cloak = 0x14,
        Backpack = 0x15,
        OuterTorso = 0x16,
        OuterLegs = 0x17,
        InnerLegs = 0x18,

        LastUserValid = 0x18,

        Mount = 0x19,
        ShopBuy = 0x1A,
        ShopResale = 0x1B,
        ShopSell = 0x1C,
        Bank = 0x1D,

        LastValid = 0x1D
    }

    public class Item : UOEntity
    {
        private ItemID m_ItemID;
        private ushort m_Amount;
        private byte m_Direction;

        private bool m_Visible;
        private bool m_Movable;

        private Layer m_Layer;
        private string m_Name;
        private object m_Parent;
        private int m_Price;
        private string m_BuyDesc;
        private List<Item> m_Items;

        private bool m_IsNew;
        private bool m_AutoStack;

        private byte[] m_HousePacket;
        private int m_HouseRev;

        private byte m_GridNum;

        public Item(Serial serial) : base(serial)
        {
            m_Items = new List<Item>();

            m_Visible = true;
            m_Movable = true;

            Agent.InvokeItemCreated(this);
        }

        public ItemID ItemID
        {
            get { return m_ItemID; }
            set { m_ItemID = value; }
        }

        public ushort Amount
        {
            get { return m_Amount; }
            set { m_Amount = value; }
        }

        public byte Direction
        {
            get { return m_Direction; }
            set { m_Direction = value; }
        }

        public bool Visible
        {
            get { return m_Visible; }
            set { m_Visible = value; }
        }

        public bool Movable
        {
            get { return m_Movable; }
            set { m_Movable = value; }
        }

        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(m_Name))
                {
                    return m_Name;
                }
                else
                {
                    return m_ItemID.ToString();
                }
            }
            set
            {
                if (value != null)
                    m_Name = value.Trim();
                else
                    m_Name = null;
            }
        }

        public Layer Layer
        {
            get
            {
                if ((m_Layer < Layer.FirstValid || m_Layer > Layer.LastValid) &&
                    ((this.ItemID.ItemData.Flags & Ultima.TileFlag.Wearable) != 0 ||
                     (this.ItemID.ItemData.Flags & Ultima.TileFlag.Armor) != 0 ||
                     (this.ItemID.ItemData.Flags & Ultima.TileFlag.Weapon) != 0
                    ))
                {
                    m_Layer = (Layer) this.ItemID.ItemData.Quality;
                }

                return m_Layer;
            }
            set { m_Layer = value; }
        }

        public Item FindItemByID(ItemID id)
        {
            return FindItemByID(id, true);
        }

        public Item FindItemByID(ItemID id, bool recurse)
        {
            for (int i = 0; i < m_Items.Count; i++)
            {
                Item item = (Item) m_Items[i];
                if (item.ItemID == id)
                {
                    return item;
                }
                else if (recurse)
                {
                    item = item.FindItemByID(id, true);
                    if (item != null)
                        return item;
                }
            }

            return null;
        }

        public Item FindItemByName(string name, bool recurse)
        {
            foreach (var i in m_Items)
            {
                Item item = i;
                if (item.ItemID.ItemData.Name.ToLower().Contains(name.ToLower()))
                {
                    return item;
                }
                else if (recurse)
                {
                    item = item.FindItemByName(name, true);
                    if (item != null)
                        return item;
                }
            }

            return null;
        }

        public int GetCount(ushort iid)
        {
            int count = 0;
            for (int i = 0; i < m_Items.Count; i++)
            {
                Item item = (Item) m_Items[i];
                if (item.ItemID == iid)
                    count += item.Amount;
                // fucking osi blank scrolls
                else if ((item.ItemID == 0x0E34 && iid == 0x0EF3) || (item.ItemID == 0x0EF3 && iid == 0x0E34))
                    count += item.Amount;
                count += item.GetCount(iid);
            }

            return count;
        }

        public object Container
        {
            get
            {
                if (m_Parent is Serial && UpdateContainer())
                    m_NeedContUpdate.Remove(this);
                return m_Parent;
            }
            set
            {
                if ((m_Parent != null && m_Parent.Equals(value))
                    || (value is Serial && m_Parent is UOEntity && ((UOEntity) m_Parent).Serial == (Serial) value)
                    || (m_Parent is Serial && value is UOEntity && ((UOEntity) value).Serial == (Serial) m_Parent))
                {
                    return;
                }

                if (m_Parent is Mobile)
                    ((Mobile) m_Parent).RemoveItem(this);
                else if (m_Parent is Item)
                    ((Item) m_Parent).RemoveItem(this);

                if (World.Player != null && (IsChildOf(World.Player.Backpack) || IsChildOf(World.Player.Quiver)))
                    Counter.Uncount(this);

                if (value is Mobile)
                    m_Parent = ((Mobile) value).Serial;
                else if (value is Item)
                    m_Parent = ((Item) value).Serial;
                else
                    m_Parent = value;

                if (!UpdateContainer() && m_NeedContUpdate != null)
                    m_NeedContUpdate.Add(this);
            }
        }

        public bool UpdateContainer()
        {
            if (!(m_Parent is Serial) || Deleted)
                return true;

            object o = null;
            Serial contSer = (Serial) m_Parent;
            if (contSer.IsItem)
                o = World.FindItem(contSer);
            else if (contSer.IsMobile)
                o = World.FindMobile(contSer);

            if (o == null)
                return false;

            m_Parent = o;

            if (m_Parent is Item)
                ((Item) m_Parent).AddItem(this);
            else if (m_Parent is Mobile)
                ((Mobile) m_Parent).AddItem(this);

            if (World.Player != null && (IsChildOf(World.Player.Backpack) || IsChildOf(World.Player.Quiver)))
            {
                bool exempt = SearchExemptionAgent.IsExempt(this);
                if (!exempt)
                    Counter.Count(this);

                if (m_IsNew)
                {
                    if (m_AutoStack)
                        AutoStackResource();

                    if (IsContainer && !exempt && (!IsPouch || !Config.GetBool("NoSearchPouches")) &&
                        Config.GetBool("AutoSearch"))
                    {
                        PacketHandlers.IgnoreGumps.Add(this);
                        PlayerData.DoubleClick(this);

                        for (int c = 0; c < Contains.Count; c++)
                        {
                            Item icheck = (Item) Contains[c];
                            if (icheck.IsContainer && !SearchExemptionAgent.IsExempt(icheck) &&
                                (!icheck.IsPouch || !Config.GetBool("NoSearchPouches")))
                            {
                                PacketHandlers.IgnoreGumps.Add(icheck);
                                PlayerData.DoubleClick(icheck);
                            }
                        }
                    }
                }
            }

            m_AutoStack = m_IsNew = false;

            return true;
        }

        private static List<Item> m_NeedContUpdate = new List<Item>();

        public static void UpdateContainers()
        {
            int i = 0;
            while (i < m_NeedContUpdate.Count)
            {
                if (((Item) m_NeedContUpdate[i]).UpdateContainer())
                    m_NeedContUpdate.RemoveAt(i);
                else
                    i++;
            }
        }

        private static List<Serial> m_AutoStackCache = new List<Serial>();

        public void AutoStackResource()
        {
            if (!IsResource || !Config.GetBool("AutoStack") || m_AutoStackCache.Contains(Serial))
                return;

            foreach (Item check in World.Items.Values)
            {
                if (check.Container == null && check.ItemID == ItemID && check.Hue == Hue &&
                    Utility.InRange(World.Player.Position, check.Position, 2))
                {
                    DragDropManager.DragDrop(this, check);
                    m_AutoStackCache.Add(Serial);
                    return;
                }
            }

            DragDropManager.DragDrop(this, World.Player.Position);
            m_AutoStackCache.Add(Serial);
        }

        public object RootContainer
        {
            get
            {
                int die = 100;
                object cont = this.Container;
                while (cont != null && cont is Item && die-- > 0)
                    cont = ((Item) cont).Container;

                return cont;
            }
        }

        public bool IsChildOf(object parent)
        {
            Serial parentSerial = 0;
            if (parent is Mobile)
                return parent == RootContainer;
            else if (parent is Item)
                parentSerial = ((Item) parent).Serial;
            else
                return false;

            object check = this;
            int die = 100;
            while (check != null && check is Item && die-- > 0)
            {
                if (((Item) check).Serial == parentSerial)
                    return true;
                else
                    check = ((Item) check).Container;
            }

            return false;
        }

        public Point3D GetWorldPosition()
        {
            int die = 100;
            object root = this.Container;
            while (root != null && root is Item && ((Item) root).Container != null && die-- > 0)
                root = ((Item) root).Container;

            if (root is Item)
                return ((Item) root).Position;
            else if (root is Mobile)
                return ((Mobile) root).Position;
            else
                return this.Position;
        }

        private void AddItem(Item item)
        {
            for (int i = 0; i < m_Items.Count; i++)
            {
                if (m_Items[i] == item)
                    return;
            }

            m_Items.Add(item);
        }

        private void RemoveItem(Item item)
        {
            m_Items.Remove(item);
        }

        public byte GetPacketFlags()
        {
            byte flags = 0;

            if (!m_Visible)
            {
                flags |= 0x80;
            }

            if (m_Movable)
            {
                flags |= 0x20;
            }

            return flags;
        }

        public void ProcessPacketFlags(byte flags)
        {
            m_Visible = ((flags & 0x80) == 0);
            m_Movable = ((flags & 0x20) != 0);
        }

        private Timer m_RemoveTimer = null;

        public void RemoveRequest()
        {
            if (m_RemoveTimer == null)
                m_RemoveTimer = Timer.DelayedCallback(TimeSpan.FromSeconds(0.25), new TimerCallback(Remove));
            else if (m_RemoveTimer.Running)
                m_RemoveTimer.Stop();

            m_RemoveTimer.Start();
        }

        public bool CancelRemove()
        {
            if (m_RemoveTimer != null && m_RemoveTimer.Running)
            {
                m_RemoveTimer.Stop();
                return true;
            }
            else
            {
                return false;
            }
        }

        public override void Remove()
        {
            /*if ( IsMulti )
                 UOAssist.PostRemoveMulti( this );*/

            List<Item> rem = new List<Item>(m_Items);
            m_Items.Clear();
            for (int i = 0; i < rem.Count; i++)
                (rem[i]).Remove();

            Counter.Uncount(this);

            if (m_Parent is Mobile)
                ((Mobile) m_Parent).RemoveItem(this);
            else if (m_Parent is Item)
                ((Item) m_Parent).RemoveItem(this);

            World.RemoveItem(this);
            base.Remove();
        }

        public List<Item> Contains
        {
            get { return m_Items; }
        }

        // possibly 4 bit x/y - 16x16?
        public byte GridNum
        {
            get { return m_GridNum; }
            set { m_GridNum = value; }
        }

        public bool OnGround
        {
            get { return Container == null; }
        }

        public bool IsContainer
        {
            get
            {
                ushort iid = m_ItemID.Value;
                return (m_Items.Count > 0 && !IsCorpse) || (iid >= 0x9A8 && iid <= 0x9AC) ||
                       (iid >= 0x9B0 && iid <= 0x9B2) ||
                       (iid >= 0xA2C && iid <= 0xA53) || (iid >= 0xA97 && iid <= 0xA9E) ||
                       (iid >= 0xE3C && iid <= 0xE43) ||
                       (iid >= 0xE75 && iid <= 0xE80 && iid != 0xE7B) || iid == 0x1E80 || iid == 0x1E81 ||
                       iid == 0x232A || iid == 0x232B ||
                       iid == 0x2B02 || iid == 0x2B03 || iid == 0x2FB7 || iid == 0x3171;
            }
        }

        public bool IsBagOfSending
        {
            get { return Hue >= 0x0400 && m_ItemID.Value == 0xE76; }
        }

        public bool IsInBank
        {
            get
            {
                if (m_Parent is Item)
                    return ((Item) m_Parent).IsInBank;
                else if (m_Parent is Mobile)
                    return this.Layer == Layer.Bank;
                else
                    return false;
            }
        }

        public bool IsNew
        {
            get { return m_IsNew; }
            set { m_IsNew = value; }
        }

        public bool AutoStack
        {
            get { return m_AutoStack; }
            set { m_AutoStack = value; }
        }

        public bool IsMulti
        {
            get { return m_ItemID.Value >= 0x4000; }
        }

        public bool IsPouch
        {
            get { return m_ItemID.Value == 0x0E79; }
        }

        public bool IsCorpse
        {
            get { return m_ItemID.Value == 0x2006 || (m_ItemID.Value >= 0x0ECA && m_ItemID.Value <= 0x0ED2); }
        }

        public bool IsDoor
        {
            get
            {
                ushort iid = m_ItemID.Value;
                return (iid >= 0x0675 && iid <= 0x06F6) || (iid >= 0x0821 && iid <= 0x0875) ||
                       (iid >= 0x1FED && iid <= 0x1FFC) ||
                       (iid >= 0x241F && iid <= 0x2424) || (iid >= 0x2A05 && iid <= 0x2A1C);
            }
        }

        public bool IsResource
        {
            get
            {
                ushort iid = m_ItemID.Value;
                return (iid >= 0x19B7 && iid <= 0x19BA) || // ore
                       (iid >= 0x09CC && iid <= 0x09CF) || // fishes
                       (iid >= 0x1BDD && iid <= 0x1BE2) || // logs
                       iid == 0x1779 || // granite / stone
                       iid == 0x11EA || iid == 0x11EB // sand
                    ;
            }
        }

        public bool IsPotion
        {
            get
            {
                return (m_ItemID.Value >= 0x0F06 && m_ItemID.Value <= 0x0F0D) ||
                       m_ItemID.Value == 0x2790 || m_ItemID.Value == 0x27DB; // Ninja belt (works like a potion)
            }
        }

        public bool IsVirtueShield
        {
            get
            {
                ushort iid = m_ItemID.Value;
                return (iid >= 0x1bc3 && iid <= 0x1bc5); // virtue shields
            }
        }

        public bool IsTwoHanded
        {
            get
            {
                ushort iid = m_ItemID.Value;

                if (Overrides.TwoHanded.TryGetValue(iid, out bool value))
                {
                    return value;
                }

                return (
                           // everything in layer 2 except shields is 2handed
                           Layer == Layer.LeftHand &&
                           !((iid >= 0x1b72 && iid <= 0x1b7b) || IsVirtueShield) // shields
                       ) ||

                       // and all of these layer 1 weapons:
                       (iid == 0x13fc || iid == 0x13fd) || // hxbow
                       (iid == 0x13AF || iid == 0x13b2) || // war axe & bow
                       (iid >= 0x0F43 && iid <= 0x0F50) || // axes & xbow
                       (iid == 0x1438 || iid == 0x1439) || // war hammer
                       (iid == 0x1442 || iid == 0x1443) || // 2handed axe
                       (iid == 0x1402 || iid == 0x1403) || // short spear
                       (iid == 0x26c1 || iid == 0x26cb) || // aos blade
                       (iid == 0x26c2 || iid == 0x26cc) || // aos bow
                       (iid == 0x26c3 || iid == 0x26cd) // aos xbow
                    ;
            }
        }

        public override string ToString()
        {
            return $"{this.Name} ({this.Serial})";
        }

        public int Price
        {
            get { return m_Price; }
            set { m_Price = value; }
        }

        public string BuyDesc
        {
            get { return m_BuyDesc; }
            set { m_BuyDesc = value; }
        }

        public int HouseRevision
        {
            get { return m_HouseRev; }
            set { m_HouseRev = value; }
        }

        public byte[] HousePacket
        {
            get { return m_HousePacket; }
            set { m_HousePacket = value; }
        }
    }
}