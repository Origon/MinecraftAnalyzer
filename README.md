# MinecraftAnalyzer

This program is designed to analyze Minecraft Bedrock worlds and list out the coordinates of specific types of blocks. It's intended use is in statistical anaysis. (But if you want to use it to cheat and find specific viens of ore, be my guest).

It is written and tested for Minecraft Bedrock 1.16.0.

It is currently programmed to check for and analyze any saved chunks within a 20 chunk radius of the world's center (0, 0). This means blocks from -320,-320 to +320,+320. This scanning pattern is hard-coded, because I couldn't be bothered to build a more complex interface, but it can be easily changed by modifying the code, which I tried to make as easy as possible.

This program takes advantage of the work done by NiclasOlofsson and his project [MiNET.LevelDB](https://github.com/NiclasOlofsson/MiNET.LevelDB). It uses his library to read the .mcworld files exported by Minecraft Bedrock. Information on the structure of the .mcworld file can be found on [this wiki page](https://minecraft.gamepedia.com/Bedrock_Edition_level_format).

This program also uses JoshClose's [CsvHelper](https://github.com/JoshClose/CsvHelper) library to create the final output file continaing the block data.
