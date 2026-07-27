using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public static class CocktailOfferGenerator
    {
        public const int OfferSize = 7;
        public const int CompatibleOfferCount = 4;
        public const int TrapOfferCount = 3;

        public static CocktailIngredientId[] Generate(
            int citySeed,
            string barId,
            int cocktailsConsumed,
            int roundNumber,
            CocktailBaseId baseId)
        {
            if (cocktailsConsumed < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cocktailsConsumed));
            }

            if (roundNumber < 1 ||
                roundNumber > CocktailMinigameSession.RoundLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(roundNumber));
            }

            CocktailRules.GetBaseDefinition(baseId);
            var compatible = new List<CocktailIngredientId>();
            var traps = new List<CocktailIngredientId>();
            foreach (CocktailIngredientDefinition definition
                     in CocktailRules.Definitions)
            {
                if (definition.IsAlcoholic)
                {
                    if (!CocktailRules.AreCompatible(baseId, definition.Id))
                    {
                        traps.Add(definition.Id);
                    }

                    continue;
                }

                if (CocktailRules.AreCompatible(baseId, definition.Id))
                {
                    compatible.Add(definition.Id);
                }
                else
                {
                    traps.Add(definition.Id);
                }
            }

            if (compatible.Count < CompatibleOfferCount ||
                traps.Count < TrapOfferCount)
            {
                throw new InvalidOperationException(
                    "The cocktail catalog cannot satisfy the offer contract.");
            }

            uint seed = BuildSeed(
                citySeed,
                barId ?? string.Empty,
                cocktailsConsumed,
                roundNumber,
                baseId);
            var random = new DeterministicRandom(seed);
            Shuffle(compatible, ref random);
            Shuffle(traps, ref random);

            var offer = new List<CocktailIngredientId>(OfferSize);
            AddFirst(offer, compatible, CompatibleOfferCount);
            AddFirst(offer, traps, TrapOfferCount);
            Shuffle(offer, ref random);
            return offer.ToArray();
        }

        private static void AddFirst(
            ICollection<CocktailIngredientId> destination,
            IList<CocktailIngredientId> source,
            int count)
        {
            for (int index = 0; index < count; index++)
            {
                destination.Add(source[index]);
            }
        }

        private static uint BuildSeed(
            int citySeed,
            string barId,
            int cocktailsConsumed,
            int roundNumber,
            CocktailBaseId baseId)
        {
            uint hash = Mix(unchecked((uint)citySeed), 0x434F434Bu);
            for (int index = 0; index < barId.Length; index++)
            {
                hash = Mix(hash, barId[index]);
            }

            hash = Mix(hash, unchecked((uint)cocktailsConsumed));
            hash = Mix(hash, unchecked((uint)roundNumber));
            return Mix(hash, unchecked((uint)baseId));
        }

        private static uint Mix(uint first, uint second)
        {
            uint hash = first ^ 0x9E3779B9u;
            hash ^= second + 0x85EBCA6Bu + (hash << 6) + (hash >> 2);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash == 0u ? 0xA341316Cu : hash;
        }

        private static void Shuffle<T>(
            IList<T> items,
            ref DeterministicRandom random)
        {
            for (int index = items.Count - 1; index > 0; index--)
            {
                int other = random.NextInt(index + 1);
                T temporary = items[index];
                items[index] = items[other];
                items[other] = temporary;
            }
        }

        private struct DeterministicRandom
        {
            private uint state;

            public DeterministicRandom(uint seed)
            {
                state = seed == 0u ? 0xA341316Cu : seed;
            }

            public int NextInt(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(exclusiveMaximum));
                }

                return (int)(
                    ((ulong)NextUInt() * (uint)exclusiveMaximum) >> 32);
            }

            private uint NextUInt()
            {
                uint value = state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                state = value;
                return value;
            }
        }
    }
}
