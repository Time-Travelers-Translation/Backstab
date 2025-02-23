using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Backstab
{
    /// <summary>
    /// Provides methods for de- and recompiling storyboards.
    /// </summary>
    public static class Storyboard
    {
        #region Get lines

        /// <summary>
        /// Parse lines of a given storyboard.
        /// </summary>
        /// <param name="storyboard">The <see cref="Stream"/> of the storyboard.</param>
        /// <returns>The parsed lines of the storyboard.</returns>
        public static IEnumerable<string> GetLines(Stream storyboard)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var data = new byte[4];
            storyboard.Position += 4;
            storyboard.Read(data);
            storyboard.Position = BinaryPrimitives.ReadInt32LittleEndian(data) + 0x38;

            var stack = new Stack<object>();
            var method = ReadMethod(storyboard);
            while (method.opCode != 0)
            {
                if (TryProduceLine(storyboard, method, stack, out var line))
                {
                    yield return line;
                }

                method = ReadMethod(storyboard);
            }
        }

        /// <summary>
        /// Read the three mandatory method values.
        /// </summary>
        /// <param name="storyboard">The <see cref="Stream"/> of the storyboard.</param>
        /// <returns>The mandatory method values.</returns>
        private static (int opCode, int subCode, int value) ReadMethod(Stream storyboard)
        {
            var data = new byte[4];

            storyboard.Read(data);
            var opCode = BinaryPrimitives.ReadInt32LittleEndian(data);
            storyboard.Read(data);
            var subCode = BinaryPrimitives.ReadInt32LittleEndian(data);
            storyboard.Read(data);
            var value = BinaryPrimitives.ReadInt32LittleEndian(data);

            return (opCode, subCode, value);
        }

        /// <summary>
        /// Tries to produce a printable line of the next values in the storyboard.
        /// </summary>
        /// <param name="storyboard">The <see cref="Stream"/> of the storyboard.</param>
        /// <param name="method">The three mandatory method values.</param>
        /// <param name="stack">The value stack for the storyboard.</param>
        /// <param name="line">The produced line.</param>
        /// <returns>If a line could be produced.</returns>
        private static bool TryProduceLine(Stream storyboard, (int opCode, int subCode, int value) method, Stack<object> stack, out string line)
        {
            line = null;
            var result = false;

            switch (method.opCode)
            {
                default:
                    throw new NotSupportedException();

                // opCodes that push something to stack
                case 0x3:
                    switch (method.subCode)
                    {
                        default:
                            throw new NotSupportedException();

                        // Integer value
                        case 0x1:
                            stack.Push(method.value);
                            break;

                        // Floating point values
                        case 0x2:
                            stack.Push(BitConverter.ToSingle(BitConverter.GetBytes(method.value)));
                            break;

                        // String values
                        case 0x3:
                            stack.Push(ReadString(storyboard, method.value + 0x58));
                            break;
                    }
                    break;

                case 0x6:
                    stack.Push(true);
                    break;

                case 0xB:
                    stack.Push(null);
                    break;

                case 0x13:
                    stack.Push((ushort)method.value);
                    break;

                // opCodes that produce a line of code
                case 0x4:
                    var value = (ushort)stack.Pop();
                    if (value == 0)
                    {
                        line = $"time = {(int)stack.Pop()};";

                        result = true;
                        break;
                    }

                    line = $"macro(0x{value:X4});";

                    result = true;
                    break;

                case 0xF:
                    line = $"exit({(int)stack.Pop()});";

                    result = true;
                    break;

                case 0x15:
                    var stack2 = new Stack<string>();
                    var suffix = false;
                    while (stack2.Count != method.subCode - 1)
                    {
                        var tmp = stack.Pop();
                        if (tmp == null)
                        {
                            suffix = true;
                            continue;
                        }
                        stack2.Push(Stringify(tmp) + (suffix ? "f" : ""));
                        suffix = false;
                    }

                    var type = PopSubMethodType(stack);
                    line = $"sub{type:000}({string.Join(", ", stack2)});";

                    result = true;
                    break;
            }

            return result;
        }

        /// <summary>
        /// Reads a zero-terminated SJIS string.
        /// </summary>
        /// <param name="storyboard">The <see cref="Stream"/> of the storyboard.</param>
        /// <param name="offset">Offset into the string region.</param>
        /// <returns>The read string.</returns>
        private static string ReadString(Stream storyboard, int offset)
        {
            var sjisEncoding = Encoding.GetEncoding("Shift-JIS");

            var backupPosition = storyboard.Position;
            storyboard.Position = offset;

            var data = Enumerable.Range(0, 999)
                .Select(_ => (byte)storyboard.ReadByte())
                .TakeWhile(b => b != 0)
                .ToArray();
            var text = sjisEncoding.GetString(data);

            storyboard.Position = backupPosition;
            return text;
        }

        /// <summary>
        /// Pops the sub method type for opCode 0x15.
        /// </summary>
        /// <param name="stack">The value stack for the storyboard.</param>
        /// <returns>The sub method type.</returns>
        private static int PopSubMethodType(Stack<object> stack)
        {
            var (value1, value2, value3) = (stack.Pop(), stack.Pop(), stack.Pop());

            if (!value1.Equals(true) || (int)value3 != 64)
            {
                throw new NotSupportedException();
            }

            return (int)value2;
        }

        /// <summary>
        /// Stringifier the given value.
        /// </summary>
        /// <param name="obj">The value to stringify.</param>
        /// <returns>The stringed value.</returns>
        static string Stringify(object obj)
        {
            switch (obj)
            {
                case float f:
                    return f.ToString("0.0000", CultureInfo.InvariantCulture);

                case int i:
                    return i.ToString();

                case string s:
                    return $"\"{s}\"";

                default:
                    throw new NotSupportedException();
            }
        }

        #endregion

        #region Get Bytes

        public static byte[] GetBytes(IList<string> lines)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var sjis = Encoding.GetEncoding("Shift-JIS");

            var scriptStart = 0x2dec + 0x38;
            var stringCorrection = 0x58;

            var args = new List<string[]>();
            foreach (var line in lines)
                args.Add(GetLineArguments(line));

            var size = GetScriptByteSize(args);

            //Write op codes

            var ms = new MemoryStream();
            var names = new MemoryStream();
            var nameOffset = 0;
            using (var nameBw = new BinaryWriter(names, sjis, true))
            using (var bw = new BinaryWriter(ms, Encoding.Default, true))
                foreach (var arg in args)
                {
                    (_, object value) = DeStringify(arg[1]);
                    switch (arg[0])
                    {
                        case "1":
                            bw.Write(0x3); bw.Write(0x1); bw.Write(0x40);
                            bw.Write(0x3); bw.Write(0x1); bw.Write((int)value);
                            bw.Write(0x6); bw.Write(0x0); bw.Write(0x0);
                            for (int i = 2; i < arg.Length; i++)
                            {
                                (int type, value) = DeStringify(arg[i]);
                                switch (type)
                                {
                                    case 1:
                                        bw.Write(0x3); bw.Write(0x1); bw.Write((int)value);
                                        break;

                                    case 2:
                                        bw.Write(0x3); bw.Write(0x2); bw.Write((float)value);
                                        break;

                                    case 3:
                                        bw.Write(0x3); bw.Write(0x3); bw.Write(scriptStart + size - stringCorrection + nameOffset);
                                        nameOffset += sjis.GetByteCount(arg[i]) - 2 + 1;
                                        nameBw.Write(sjis.GetBytes(arg[i].Split('\"')[1] + "\0"));
                                        break;

                                    case 4:
                                        bw.Write(0x3); bw.Write(0x2); bw.Write((float)value);
                                        bw.Write(0xb); bw.Write(0x0); bw.Write(0x0);
                                        break;

                                    case 5:
                                        bw.Write(0x3); bw.Write(0x1); bw.Write((int)value);
                                        bw.Write(0xb); bw.Write(0x0); bw.Write(0x0);
                                        break;
                                }
                            }
                            bw.Write(0x15); bw.Write(arg.Count() - 1); bw.Write(0x0);
                            break;
                        case "2":
                            (_, value) = DeStringify(arg[1]);
                            bw.Write(0x13); bw.Write(0x0); bw.Write((int)value);
                            bw.Write(0x4); bw.Write(0x0); bw.Write(0x0);
                            break;
                        case "3":
                            (_, value) = DeStringify(arg[1]);
                            bw.Write(0x3); bw.Write(0x1); bw.Write((int)value);
                            bw.Write(0x13); bw.Write(0x0); bw.Write(0x0);
                            bw.Write(0x4); bw.Write(0x0); bw.Write(0x0);
                            break;
                        case "4":
                            (_, value) = DeStringify(arg[1]);
                            bw.Write(0x3); bw.Write(0x1); bw.Write((int)value);
                            bw.Write(0xf); bw.Write(0x0); bw.Write(0x0);
                            bw.Write(0x0); bw.Write(0x0); bw.Write(0x0);
                            break;
                    }
                }

            //Write finished stuff
            ms.Position = 0;
            names.Position = 0;
            var result = new MemoryStream();
            ms.CopyTo(result);
            names.CopyTo(result);

            return result.ToArray();
        }

        static string[] GetLineArguments(string line)
        {
            var result = new List<string>();

            var type = GetString(line.Take(3));
            switch (type)
            {
                case "sub":
                    result.Add("1");
                    result.Add(GetString(line.Where((c, i) => i >= 3 && i <= 5)));

                    var startIndex = line.IndexOf('(') + 1;
                    var endIndex = line.LastIndexOf(')');
                    result.AddRange(line[startIndex..endIndex].Split(new[] { ", " }, StringSplitOptions.None));
                    break;
                case "mac":
                    result.Add("2");

                    var startIndex1 = line.IndexOf('(') + 1;
                    var endIndex1 = line.LastIndexOf(')');
                    result.Add(line[startIndex1..endIndex1]);
                    break;
                case "tim":
                    result.Add("3");
                    result.Add(line.Split(new[] { " = " }, StringSplitOptions.None)[1].Split(';')[0]);
                    break;
                case "exi":
                    result.Add("4");

                    var startIndex2 = line.IndexOf('(') + 1;
                    var endIndex2 = line.LastIndexOf(')');
                    result.Add(line[startIndex2..endIndex2]);
                    break;
                default:
                    throw new NotSupportedException();
            }

            return result.ToArray();
        }

        static string GetString(IEnumerable<char> ec) => ec.Aggregate("", (output, c) => output + c);

        static int GetScriptByteSize(List<string[]> args)
        {
            int result = 0;

            foreach (var line in args)
                switch (line[0])
                {
                    case "1":
                        result += 2 * 0xc + 0xc + ((line.Count() - 1) * 0xc);   //2 constants 0x40 and true; sub type; sub arguments;
                        if (line.ToList().Find(l => !l.Contains("\"") && l.Contains("f")) != null)
                            result += 0xc * line.ToList().Where(l => !l.Contains("\"") && l.Contains("f")).Count();
                        break;
                    case "2":
                        result += 2 * 0xc;
                        break;
                    case "3":
                        result += 3 * 0xc;
                        break;
                    case "4":
                        result += 3 * 0xc;
                        break;
                }

            return result;
        }

        //sub, value
        static (int, object) DeStringify(string input)
        {
            // Detect string value
            if (Regex.IsMatch(input, "^\".*\"$"))
                return (3, 0);

            // Detect null preceeded float value
            if (Regex.IsMatch(input, @"^-?[\d]+\.[\d]+f$"))
                return (4, float.Parse(input[..^1], CultureInfo.InvariantCulture));

            // Detect null preceeded int value
            if (Regex.IsMatch(input, @"^[\d]+f$"))
                return (5, int.Parse(input[..^1], CultureInfo.InvariantCulture));

            // Detect float value
            if (Regex.IsMatch(input, @"^-?[\d]+\.[\d]+$"))
                return (2, float.Parse(input, CultureInfo.InvariantCulture));

            // Detect integer hex value
            if (input.StartsWith("0x"))
                return (1, int.Parse(input[2..], NumberStyles.HexNumber));

            // Parse normal integer value
            return (1, int.Parse(input));
        }

        #endregion

        public static void InjectAndCorrect(string STBName, byte[] newSTB)
        {
            var originalStbData = File.ReadAllBytes(STBName);

            var file = new MemoryStream();
            file.Write(originalStbData);

            using (var br = new BinaryReader(file, Encoding.Default, true))
            using (var bw = new BinaryWriter(file, Encoding.Default, true))
            {
                br.BaseStream.Position = 4;
                var scriptStart = br.ReadInt32();
                var headerSize = br.ReadInt32();
                var scriptMetaStart = br.ReadInt32();
                var metaCount = br.ReadInt32();

                br.BaseStream.Position = scriptMetaStart;
                var meta = new List<(int, int)>();
                for (int i = 0; i < metaCount; i++) 
                    meta.Add((br.ReadInt32(), br.ReadInt32()));

                var index = meta.FindIndex(m => m.Item2 == scriptStart);
                var actualChange = ((newSTB.Length + 0x3) & ~0x3) - (meta[index + 1].Item2 - (meta[index].Item2 + 0x38));

                if (actualChange == 0)
                {
                    br.BaseStream.Position = scriptStart + 0x38;
                    bw.Write(newSTB);
                }
                else
                {
                    for (int i = index + 1; i < meta.Count; i++)
                    {
                        br.BaseStream.Position = meta[i].Item2;
                        var off = br.ReadInt32(); br.BaseStream.Position -= 4;
                        bw.Write(off + actualChange);

                        br.BaseStream.Position += 0x34;
                        while (true)
                        {
                            var (opcode, sub, val) = (br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
                            if (opcode == 0x3 && sub == 0x3)
                            {
                                br.BaseStream.Position -= 4;
                                bw.Write(val + actualChange);
                            }
                            if (opcode == 0xf)
                                break;
                        }
                    }
                    br.BaseStream.Position = meta[index + 1].Item2;
                    var toChange = br.ReadBytes((int)br.BaseStream.Length - meta[index + 1].Item2);

                    br.BaseStream.Position = scriptStart + 0x38;
                    bw.Write(newSTB);

                    bw.BaseStream.Position = (bw.BaseStream.Position + 0x3) & ~0x3;
                    bw.Write(toChange);

                    br.BaseStream.Position = scriptMetaStart + (index + 1) * 0x8;
                    for (int i = index + 1; i < meta.Count; i++)
                    {
                        (int id, int offset) = (br.ReadInt32(), br.ReadInt32());
                        br.BaseStream.Position -= 4;
                        bw.Write(offset + actualChange);
                    }
                }
            }

            File.WriteAllBytes(STBName, file.ToArray());
        }
    }
}
