using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace ADUserManager
{
    public static class PasswordTools
    {
        const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // no I, O
        const string Lower = "abcdefghijkmnopqrstuvwxyz";  // no l
        const string Digits = "23456789";                  // no 0, 1
        const string Symbols = "!#$%&*?@+=-";

        static readonly string[] Words =
        {
            "Apple","Amber","Anchor","Arrow","Autumn","Badger","Banjo","Beacon","Berry","Birch",
            "Blossom","Bramble","Breeze","Bridge","Bronze","Bubble","Cactus","Candle","Canyon","Carrot",
            "Castle","Cedar","Cherry","Cloud","Clover","Cobalt","Comet","Copper","Coral","Cosmos",
            "Daisy","Dolphin","Dragon","Eagle","Echo","Ember","Falcon","Fern","Forest","Fossil",
            "Garden","Glacier","Ginger","Granite","Harbour","Hazel","Heron","Honey","Island","Ivory",
            "Jasmine","Jungle","Kettle","Kite","Lantern","Lemon","Lilac","Lotus","Magnet","Mango",
            "Maple","Marble","Meadow","Meteor","Mint","Monkey","Nectar","Noble","Ocean","Olive",
            "Orbit","Otter","Panda","Pebble","Pepper","Piano","Pillow","Planet","Plum","Pocket",
            "Puffin","Quartz","Rabbit","Rainbow","Raven","River","Robin","Rocket","Saffron","Salmon",
            "Silver","Sparrow","Spruce","Summit","Sunset","Thunder","Tiger","Toffee","Tulip","Velvet",
            "Violet","Walnut","Willow","Winter","Zebra"
        };

        static readonly RNGCryptoServiceProvider Rng = new RNGCryptoServiceProvider();

        static int Next(int max)
        {
            // Unbiased random integer in [0, max)
            byte[] b = new byte[4];
            uint limit = uint.MaxValue - (uint.MaxValue % (uint)max);
            uint v;
            do
            {
                Rng.GetBytes(b);
                v = BitConverter.ToUInt32(b, 0);
            } while (v >= limit);
            return (int)(v % (uint)max);
        }

        static char Pick(string set) { return set[Next(set.Length)]; }

        public static string Generate(string style, int length)
        {
            if (length < 8) length = 8;
            if (string.Equals(style, "Random", StringComparison.OrdinalIgnoreCase))
                return GenerateRandom(length);
            return GenerateMemorable(length);
        }

        public static string GenerateRandom(int length)
        {
            var chars = new List<char> { Pick(Upper), Pick(Lower), Pick(Digits), Pick(Symbols) };
            string all = Upper + Lower + Digits + Symbols;
            while (chars.Count < length) chars.Add(Pick(all));
            // Fisher-Yates shuffle
            for (int i = chars.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                char t = chars[i]; chars[i] = chars[j]; chars[j] = t;
            }
            return new string(chars.ToArray());
        }

        public static string GenerateMemorable(int length)
        {
            // Word-Word99!  e.g. Maple-Tiger-47!
            var sb = new StringBuilder();
            sb.Append(Words[Next(Words.Length)]);
            do
            {
                sb.Append('-');
                sb.Append(Words[Next(Words.Length)]);
            } while (sb.Length + 4 < length);
            sb.Append('-');
            sb.Append(Pick(Digits)).Append(Pick(Digits));
            sb.Append(Pick("!#$%&*?@"));
            return sb.ToString();
        }

        /// <summary>
        /// Checks a password against the domain policy that can be read from AD.
        /// Returns null when acceptable, otherwise a human readable reason.
        /// </summary>
        public static string Validate(string pwd, int minLength, bool complexity, string sam, string displayName)
        {
            if (string.IsNullOrEmpty(pwd)) return "Enter a password.";
            if (minLength < 1) minLength = 1;
            if (pwd.Length < minLength)
                return "Must be at least " + minLength + " characters (currently " + pwd.Length + ").";
            if (pwd.Length > 127) return "Password is too long.";

            if (complexity)
            {
                int cats = 0;
                bool u = false, l = false, d = false, s = false;
                foreach (char c in pwd)
                {
                    if (char.IsUpper(c)) u = true;
                    else if (char.IsLower(c)) l = true;
                    else if (char.IsDigit(c)) d = true;
                    else s = true;
                }
                if (u) cats++; if (l) cats++; if (d) cats++; if (s) cats++;
                if (cats < 3)
                    return "Must contain 3 of: upper case, lower case, number, symbol.";

                if (!string.IsNullOrEmpty(sam) && sam.Length >= 3 &&
                    pwd.IndexOf(sam, StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Must not contain the username.";

                if (!string.IsNullOrEmpty(displayName))
                {
                    foreach (string token in displayName.Split(new[] { ' ', ',', '.', '-', '_', '#', '\t' },
                                 StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (token.Length >= 3 && pwd.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                            return "Must not contain part of the user's name (\"" + token + "\").";
                    }
                }
            }
            return null;
        }
    }
}
