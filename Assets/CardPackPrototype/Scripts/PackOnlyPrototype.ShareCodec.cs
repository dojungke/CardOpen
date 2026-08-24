using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace CardOpen.Prototype
{
    public sealed partial class PackOnlyPrototype
    {
        private static string EncodeSharedResultBinary(SharedResultData result)
        {
            byte[] raw;
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write((byte)2);
                WriteVarUInt(writer, (uint)Mathf.Max(0, result.TotalScore));
                WriteVarUInt(writer, (uint)Mathf.Max(0, result.RoundScore));
                WriteVarUInt(writer, (uint)Mathf.Max(0, result.GoalIndex));
                WriteVarUInt(writer, (uint)Mathf.Max(0, result.CompletedPacks));
                writer.Write(result.Cleared);
                int deckCount = result.Deck != null ? result.Deck.Length : 0;
                WriteVarUInt(writer, (uint)deckCount);
                for (int i = 0; i < deckCount; i++) WriteSharedCard(writer, result.Deck[i]);
                writer.Flush();
                raw = stream.ToArray();
            }

            using (MemoryStream output = new MemoryStream())
            {
                using (DeflateStream deflate = new DeflateStream(output,
                    System.IO.Compression.CompressionLevel.Optimal, true))
                    deflate.Write(raw, 0, raw.Length);
                return "2." + ToBase64Url(output.ToArray());
            }
        }

        private static SharedResultData DecodeSharedResultBinary(string payload)
        {
            string decodedPayload = Uri.UnescapeDataString(payload);
            if (!decodedPayload.StartsWith("2.", StringComparison.Ordinal))
                throw new FormatException("Unsupported compact share format.");
            byte[] compressed = FromBase64Url(decodedPayload.Substring(2));
            byte[] raw;
            using (MemoryStream input = new MemoryStream(compressed))
            using (DeflateStream deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                byte[] buffer = new byte[4096];
                int total = 0;
                int read;
                while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > 262144) throw new InvalidDataException("Shared result is too large.");
                    output.Write(buffer, 0, read);
                }
                raw = output.ToArray();
            }

            Dictionary<uint, string> resourceNames = BuildSharedResourceNameLookup();
            using (MemoryStream stream = new MemoryStream(raw))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadByte() != 2) throw new InvalidDataException("Unsupported share version.");
                SharedResultData result = new SharedResultData
                {
                    Version = 1,
                    TotalScore = ReadNonNegativeInt(reader),
                    RoundScore = ReadNonNegativeInt(reader),
                    GoalIndex = ReadNonNegativeInt(reader),
                    CompletedPacks = ReadNonNegativeInt(reader),
                    Cleared = reader.ReadBoolean()
                };
                int deckCount = ReadCount(reader, 5);
                result.Deck = new SharedCardData[deckCount];
                for (int i = 0; i < deckCount; i++)
                    result.Deck[i] = ReadSharedCard(reader, resourceNames, 0);
                return result;
            }
        }

        private static void WriteSharedCard(BinaryWriter writer, SharedCardData card)
        {
            if (card == null)
            {
                writer.Write((byte)0);
                return;
            }
            writer.Write((byte)(1 | (card.IsHolographic ? 2 : 0)));
            writer.Write(GetStableCardResourceId(card.ResourceName));
            writer.Write((byte)Mathf.Clamp(card.Color, 0, byte.MaxValue));
            writer.Write((byte)Mathf.Clamp(card.Number, 0, byte.MaxValue));
            writer.Write((byte)Mathf.Clamp(card.Rarity, 0, byte.MaxValue));
            WriteVarInt(writer, card.DeckSlot);
            WriteVarUInt(writer, (uint)Mathf.Max(1, card.CombinedCopies));
            WriteVarUInt(writer, (uint)Mathf.Max(0, card.CombinedHolographicCopies));
            WriteSharedCard(writer, card.EquippedMagic);
            WriteSharedCard(writer, card.EquippedWeapon);
            int relicCount = card.InheritedRelics != null ? card.InheritedRelics.Length : 0;
            WriteVarUInt(writer, (uint)relicCount);
            for (int i = 0; i < relicCount; i++) WriteSharedCard(writer, card.InheritedRelics[i]);
            WriteSharedIntValues(writer, card.AccumulatedFlatScore);
            WriteSharedIntValues(writer, card.RemainingDraws);
            WriteSharedIntValues(writer, card.Stacks);
            WriteSharedIntValues(writer, card.PerPackTriggers);
            WriteSharedIntValues(writer, card.PacksElapsed);
            WriteSharedFloatValues(writer, card.AccumulatedPercent);
        }

        private static SharedCardData ReadSharedCard(BinaryReader reader,
            Dictionary<uint, string> resourceNames, int depth)
        {
            if (depth > 12) throw new InvalidDataException("Shared card nesting is too deep.");
            byte flags = reader.ReadByte();
            if ((flags & 1) == 0) return null;
            uint resourceId = reader.ReadUInt32();
            if (!resourceNames.TryGetValue(resourceId, out string resourceName))
                throw new InvalidDataException("Shared card asset could not be found.");
            SharedCardData card = new SharedCardData
            {
                ResourceName = resourceName,
                Color = reader.ReadByte(),
                Number = reader.ReadByte(),
                Rarity = reader.ReadByte(),
                DeckSlot = ReadVarInt(reader),
                CombinedCopies = ReadNonNegativeInt(reader),
                CombinedHolographicCopies = ReadNonNegativeInt(reader),
                IsHolographic = (flags & 2) != 0
            };
            card.EquippedMagic = ReadSharedCard(reader, resourceNames, depth + 1);
            card.EquippedWeapon = ReadSharedCard(reader, resourceNames, depth + 1);
            int relicCount = ReadCount(reader, 32);
            card.InheritedRelics = new SharedCardData[relicCount];
            for (int i = 0; i < relicCount; i++)
                card.InheritedRelics[i] = ReadSharedCard(reader, resourceNames, depth + 1);
            card.AccumulatedFlatScore = ReadSharedIntValues(reader);
            card.RemainingDraws = ReadSharedIntValues(reader);
            card.Stacks = ReadSharedIntValues(reader);
            card.PerPackTriggers = ReadSharedIntValues(reader);
            card.PacksElapsed = ReadSharedIntValues(reader);
            card.AccumulatedPercent = ReadSharedFloatValues(reader);
            return card;
        }

        private static void WriteSharedIntValues(BinaryWriter writer, SharedIntValue[] values)
        {
            int count = values != null ? values.Length : 0;
            WriteVarUInt(writer, (uint)count);
            for (int i = 0; i < count; i++)
            {
                SharedIntValue value = values[i];
                WriteVarInt(writer, value != null ? value.Key : 0);
                WriteVarInt(writer, value != null ? value.Value : 0);
            }
        }

        private static SharedIntValue[] ReadSharedIntValues(BinaryReader reader)
        {
            int count = ReadCount(reader, 256);
            SharedIntValue[] values = new SharedIntValue[count];
            for (int i = 0; i < count; i++)
                values[i] = new SharedIntValue { Key = ReadVarInt(reader), Value = ReadVarInt(reader) };
            return values;
        }

        private static void WriteSharedFloatValues(BinaryWriter writer, SharedFloatValue[] values)
        {
            int count = values != null ? values.Length : 0;
            WriteVarUInt(writer, (uint)count);
            for (int i = 0; i < count; i++)
            {
                SharedFloatValue value = values[i];
                WriteVarInt(writer, value != null ? value.Key : 0);
                writer.Write(value != null ? value.Value : 0f);
            }
        }

        private static SharedFloatValue[] ReadSharedFloatValues(BinaryReader reader)
        {
            int count = ReadCount(reader, 256);
            SharedFloatValue[] values = new SharedFloatValue[count];
            for (int i = 0; i < count; i++)
                values[i] = new SharedFloatValue { Key = ReadVarInt(reader), Value = reader.ReadSingle() };
            return values;
        }

        private static void WriteVarInt(BinaryWriter writer, int value)
        {
            WriteVarUInt(writer, unchecked((uint)((value << 1) ^ (value >> 31))));
        }

        private static int ReadVarInt(BinaryReader reader)
        {
            uint value = ReadVarUInt(reader);
            return unchecked((int)(value >> 1) ^ -((int)value & 1));
        }

        private static void WriteVarUInt(BinaryWriter writer, uint value)
        {
            while (value >= 0x80)
            {
                writer.Write((byte)(value | 0x80));
                value >>= 7;
            }
            writer.Write((byte)value);
        }

        private static uint ReadVarUInt(BinaryReader reader)
        {
            uint value = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                byte current = reader.ReadByte();
                value |= (uint)(current & 0x7f) << shift;
                if ((current & 0x80) == 0) return value;
            }
            throw new InvalidDataException("Invalid variable-length integer.");
        }

        private static int ReadNonNegativeInt(BinaryReader reader)
        {
            uint value = ReadVarUInt(reader);
            if (value > int.MaxValue) throw new InvalidDataException("Shared number is too large.");
            return (int)value;
        }

        private static int ReadCount(BinaryReader reader, int maximum)
        {
            int count = ReadNonNegativeInt(reader);
            if (count > maximum) throw new InvalidDataException("Shared collection is too large.");
            return count;
        }

        private static uint GetStableCardResourceId(string resourceName)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(resourceName ?? string.Empty);
            uint hash = 2166136261u;
            for (int i = 0; i < bytes.Length; i++) hash = unchecked((hash ^ bytes[i]) * 16777619u);
            return hash;
        }

        private static Dictionary<uint, string> BuildSharedResourceNameLookup()
        {
            global::CardData[] resources = Resources.LoadAll<global::CardData>(string.Empty);
            Dictionary<uint, string> lookup = new Dictionary<uint, string>();
            for (int i = 0; i < resources.Length; i++)
            {
                global::CardData resource = resources[i];
                if (resource == null) continue;
                uint id = GetStableCardResourceId(resource.name);
                if (lookup.TryGetValue(id, out string existing) && existing != resource.name)
                    throw new InvalidDataException("Card resource ID collision.");
                lookup[id] = resource.name;
            }
            return lookup;
        }
    }
}
