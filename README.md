# MinecraftAnalyzer

This program is designed to analyze Minecraft worlds and list out the coordinates of specific types of blocks. It's intended use is in statistical anaysis. (But if you want to use it to cheat and find specific viens of ore, be my guest).

It was originally written and tested for Minecraft version 1.16, supporting both Bedrock and Java editions. It has been updated to support Java 1.18 (and may work for Bedrock was well, but is currently untested).

In Bedrock mode, it is currently programmed to check for and analyze any saved chunks within a 20 chunk radius of the world's center (0, 0). This means blocks from -320,-320 to +320,+320. This scanning pattern is hard-coded, because I couldn't be bothered to build a more complex interface, but it can be easily changed by modifying the code, which I tried to make as easy as possible.

In Java mode, the program is likewise hardcoded, but the range is different due to the different file system. Java mode currently processes any chunks within a 2 region radius (1 region = 32 square chunks = 262,144 square blocks; so nine of those). This makes the output size significantly larger than Bedrock mode.

This program takes advantage of the work done by NiclasOlofsson and his project [MiNET.LevelDB](https://github.com/NiclasOlofsson/MiNET.LevelDB). It uses his library to read the .mcworld files exported by Minecraft Bedrock. Information on the structure of the .mcworld file can be found on [this wiki page](https://minecraft.gamepedia.com/Bedrock_Edition_level_format).

This program makes use of a customized copy of the [fNBT](https://github.com/mstefarov/fNbt) repo. I needed to add support for the "Long Array" tag, since the main repo (and the NuGet package) didn't include it yet at the time of development. The repo has since added it, but the NuGet package remains unupdated at time of writing.

This program also uses JoshClose's [CsvHelper](https://github.com/JoshClose/CsvHelper) library to create the final output file continaing the block data.
