using System.IO;

namespace Backstab
{
    class Program
    {
        static void Main(string[] args)
        {
            var path = args[0];
            if (Directory.Exists(path))
            {
                foreach (var filePath in Directory.GetFiles(path))
                    ParseFile(filePath);
            }
            else if (File.Exists(path))
            {
                ParseFile(path);
            }
        }

        /// <summary>
        /// Parses a given input file.
        /// </summary>
        /// <param name="path">The path of the file to parse.</param>
        static void ParseFile(string path)
        {
            switch (Path.GetExtension(path))
            {
                case ".stb":
                    ToTxt(path);
                    break;

                case ".txt":
                    ToStb(path);
                    break;
            }
        }

        /// <summary>
        /// Converts a Storyboard to a text file.
        /// </summary>
        /// <param name="path">The path to the Storyboard.</param>
        static void ToTxt(string path)
        {
            using var stbStream = File.OpenRead(path);
            var lines = Storyboard.GetLines(stbStream);

            File.WriteAllLines(path + ".txt", lines);
        }

        /// <summary>
        /// Converts a text file to a Storyboard.
        /// </summary>
        /// <param name="path">The path to the text file.</param>
        static void ToStb(string path)
        {
            var stbData = Storyboard.GetBytes(File.ReadAllLines(path));

            Storyboard.InjectAndCorrect(Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path)), stbData);
        }
    }
}
