# Backstab
An extractor and injector of .stb storyboard files for "Time Travelers".

## Usage

This is a command line tool and has to be executed on the command line with the proper arguments.

A single argument has to be provided. That argument represents a file path or folder path.<br>
The tool will try to extract any file in the given folder path (or the file path) with extension .stb and convert it to a .txt.<br>
The same happens vice versa, for any file with extenion .txt it injects it back to the corresponding .stb.

**Attention**: The injection relies on finding the corresponding .stb file to inject into in the same folder as the input file.

Examples:
```
Backstab.exe Path/to/scene.stb        -> Extracts to Path/to/scene.stb.txt
Backstab.exe Path/to/scene.stb.txt    -> Injects to Path/to/scene.stb, if it exists
Backstab.exe Path/to/scenes_folder    -> Extracts all .stb to .txt in Path/to/scene_folder
                                      -> Also injects all .txt into .stb
```

## Credit

The initial source code [here](https://github.com/adibsurani/Backstab).<br>
This version allows for reinjecting the extracted instructions of the storyboards.
