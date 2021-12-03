using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.IO;
using MiNET.LevelDB;
using NUnit.Framework;
using CsvHelper;
using System.Globalization;
using Microsoft.Win32;
using System.Threading;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace MinecraftAnalyzer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        public bool Running
        {
            get { return (bool)GetValue(RunningProperty); }
            private set { SetValue(RunningPropertyKey, value); }
        }
        public static readonly DependencyPropertyKey RunningPropertyKey =
            DependencyProperty.RegisterReadOnly("Running", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
        public static readonly DependencyProperty RunningProperty = RunningPropertyKey.DependencyProperty;

        public bool UseMultithreading
        {
            get { return (bool)GetValue(UseMultithreadingProperty); }
            set { SetValue(UseMultithreadingProperty, value); }
        }
        public static readonly DependencyProperty UseMultithreadingProperty =
            DependencyProperty.Register("UseMultithreading", typeof(bool), typeof(MainWindow));

        public MinecraftEdition MinecraftEdition
        {
            get { return (MinecraftEdition)GetValue(MinecraftEditionProperty); }
            set { SetValue(MinecraftEditionProperty, value); }
        }
        public static readonly DependencyProperty MinecraftEditionProperty =
            DependencyProperty.Register("MinecraftEdition", typeof(MinecraftEdition), typeof(MainWindow),
                                        new PropertyMetadata(MinecraftEdtiionChanged));

        private static void MinecraftEdtiionChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var w = (MainWindow)o;
            w.InputFile = null;
            w.OutputFile = null;
        }

        public string InputFile
        {
            get { return (string)GetValue(InputFileProperty); }
            set { SetValue(InputFileProperty, value); }
        }
        public static readonly DependencyProperty InputFileProperty =
            DependencyProperty.Register("InputFile", typeof(string), typeof(MainWindow));

        public string OutputFile
        {
            get { return (string)GetValue(OutputFileProperty); }
            set { SetValue(OutputFileProperty, value); }
        }
        public static readonly DependencyProperty OutputFileProperty =
            DependencyProperty.Register("OutputFile", typeof(string), typeof(MainWindow));

        private void B_BrowseForInput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();

            if (MinecraftEdition == MinecraftEdition.Bedrock)
            {
                dialog.Filter = "Minecraft Bedrock World (*.mcworld)|*.mcworld";
            }
            else
            {
                dialog.Filter = "Minecraft Java Main Level File (level.dat)|level.dat";
            }

            if ((bool)dialog.ShowDialog())
            {
                InputFile = dialog.FileName;
            }
        }

        private void B_BrowseForOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog();
            dialog.Filter = "Comma-seperated text file (*.csv)|*.csv";

            if ((bool)dialog.ShowDialog())
            {
                OutputFile = dialog.FileName;
            }
        }

        public static readonly RoutedCommand AnalyzeCommand = new RoutedCommand("Analyze", typeof(MainWindow));

        private void CanExecuteAnalyze(object sender, CanExecuteRoutedEventArgs e)
        {
            e.Handled = true;
            e.CanExecute = !Running && InputFile != null && OutputFile != null;
        }

        private async void ExecuteAnalyze(object sender, ExecutedRoutedEventArgs e)
        {
            e.Handled = true;
            Running = true;
            var s = Stopwatch.StartNew();
            if (MinecraftEdition == MinecraftEdition.Bedrock)
            {
                await AnalyzeBedrockChunksAroundCenterAsync(20, UseMultithreading);
            }
            else
            {
                await AnalyzeJavaRegionsAroundCenterAsync(2, UseMultithreading);
            }
            s.Stop();
            MessageBox.Show(
$@"Completed after {Math.Floor(s.Elapsed.TotalMinutes)} minute(s) and {s.Elapsed.Seconds} second(s).
Evaluated {blockCount} blocks.");
            Running = false;
        }

        public void AnalyzeBedrockChunksAroundCenter(int chunksRadiusToCheck, bool multiThread = true)
        {
            Assert.IsTrue(BitConverter.IsLittleEndian);

            string inputFile = null;
            string outputFile = null;

            if (Dispatcher.CheckAccess())
            {
                inputFile = InputFile;
                outputFile = OutputFile;
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    inputFile = InputFile;
                    outputFile = OutputFile;
                });
            }

            using (var db = new Database(new DirectoryInfo(inputFile)))
            using (var baseWriter = new StreamWriter(outputFile, false))
            using (var csvWriter = new CsvWriter(baseWriter, CultureInfo.CurrentCulture))
            {
                db.Open();

                csvWriter.Configuration.RegisterClassMap<BlockInfoCsvMap>();

                blockQueue = new ConcurrentQueue<BlockInfo>();

                var chunkTasks = new List<Task>();

                for (int x = 0; x < chunksRadiusToCheck; x++)
                {
                    for (int z = 0; z < chunksRadiusToCheck; z++)
                    {
                        DoParseChunkIfExists(db, x, z, QueueBlock, multiThread, chunkTasks);
                        if (z > 0) { DoParseChunkIfExists(db, x, z * -1, QueueBlock, multiThread, chunkTasks); }
                        if (x > 0) { DoParseChunkIfExists(db, x * -1, z, QueueBlock, multiThread, chunkTasks); }
                        if (x > 0 && z > 0) { DoParseChunkIfExists(db, x * -1, z * -1, QueueBlock, multiThread, chunkTasks); }
                    }
                }

                Task.WaitAll(chunkTasks.ToArray());

                csvWriter.WriteRecords(blockQueue);
            }
        }

        public static void DoParseChunkIfExists(Database db, int x, int z, Action<BlockInfo> blockAction, bool multiThread, List<Task> chunkTasks)
        {
            if (multiThread)
            {
                chunkTasks.Add(BedrockAnalyzer.ParseChunkIfExistsAsync(db, new ChunkCoordinate(x, z), blockAction));
            }
            else
            {
                BedrockAnalyzer.ParseChunkIfExists(db, new ChunkCoordinate(x, z), blockAction);
            }
        }

        public Task AnalyzeBedrockChunksAroundCenterAsync(int chunksRadiusToCheck, bool multiThread = true)
        {
            return Task.Run(() => AnalyzeBedrockChunksAroundCenter(chunksRadiusToCheck, multiThread));
        }

        public void AnalyzeJavaRegionsAroundCenter(int regionRadiusToCheck, bool multiThread = true)
        {
            Assert.IsTrue(BitConverter.IsLittleEndian);

            string inputFile = null;
            string outputFile = null;

            if (Dispatcher.CheckAccess())
            {
                inputFile = InputFile;
                outputFile = OutputFile;
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    inputFile = InputFile;
                    outputFile = OutputFile;
                });
            }

            var path = new DirectoryInfo(System.IO.Path.GetDirectoryName(inputFile));

            using (var baseWriter = new StreamWriter(outputFile, false))
            using (var csvWriter = new CsvWriter(baseWriter, CultureInfo.CurrentCulture))
            {
                csvWriter.Configuration.RegisterClassMap<BlockInfoCsvMap>();

                blockQueue = new ConcurrentQueue<BlockInfo>();
                blockCount = 0;

                var regionTasks = new List<Task>();

                for (int x = 0; x < regionRadiusToCheck; x++)
                {
                    for (int z = 0; z < regionRadiusToCheck; z++)
                    {
                        DoParseRegionIfExists(path, x, z, QueueBlock, multiThread, regionTasks);
                        if (z > 0) { DoParseRegionIfExists(path, x, z * -1, QueueBlock, multiThread, regionTasks); }
                        if (x > 0) { DoParseRegionIfExists(path, x * -1, z, QueueBlock, multiThread, regionTasks); }
                        if (x > 0 && z > 0) { DoParseRegionIfExists(path, x * -1, z * -1, QueueBlock, multiThread, regionTasks); }
                    }
                }

                Task.WaitAll(regionTasks.ToArray());

                csvWriter.WriteRecords(blockQueue);
            }
        }

        private static void DoParseRegionIfExists(DirectoryInfo path, int x, int z, Action<BlockInfo> blockAction, bool multiThread, List<Task> regionTasks)
        {
            Debug.WriteLine($"Trying to parse region {x},{z}");

            if (multiThread)
            {
                regionTasks.Add(JavaAnalyzer.ParseRegionIfExistsAsync(path, new ChunkCoordinate(x, z), blockAction, multiThread));
            }
            else
            {
                JavaAnalyzer.ParseRegionIfExists(path, new ChunkCoordinate(x, z), blockAction, multiThread);
            }
        }

        public Task AnalyzeJavaRegionsAroundCenterAsync(int chunksRadiusToCheck, bool multiThread = true)
        {
            return Task.Run(() => AnalyzeJavaRegionsAroundCenter(chunksRadiusToCheck, multiThread));
        }

        public class BlockInfoCsvMap : CsvHelper.Configuration.ClassMap<BlockInfo>
        {
            public BlockInfoCsvMap()
            {
                Map(b => b.Coordinates, false).Name("X").ConvertUsing(row => row.Coordinates.X.ToString());
                Map(b => b.Coordinates, false).Name("Y").ConvertUsing(row => row.Coordinates.Y.ToString());
                Map(b => b.Coordinates, false).Name("Z").ConvertUsing(row => row.Coordinates.Z.ToString());
                Map(b => b.State, false).Name("Block").ConvertUsing(row => row.Name);
            }
        }

        private string[] oreBlocks = { "minecraft:gold_ore",
                                       "minecraft:deepslate_gold_ore",
                                       "minecraft:raw_gold_block",
                                       "minecraft:iron_ore",
                                       "minecraft:deepslate_iron_ore",
                                       "minecraft:raw_iron_block",
                                       "minecraft:coal_ore",
                                       "minecraft:deepslate_coal_ore",
                                       "minecraft:lapis_ore",
                                       "minecraft:deepslate_lapis_ore",
                                       "minecraft:diamond_ore",
                                       "minecraft:deepslate_diamond_ore",
                                       "minecraft:redstone_ore",
                                       "minecraft:deepslate_redstone_ore",
                                       "minecraft:lit_redstone_ore",
                                       "minecraft:lit_deepslate_redstone_ore",
                                       "minecraft:copper_ore",
                                       "minecraft:deepslate_copper_ore",
                                       "minecraft:raw_copper_block",
                                       "minecraft:emerald_ore",
                                       "minecraft:deepslate_emerald_ore",
                                       "minecraft:lava",
                                       "minecraft:flowing_lava"};

        private ConcurrentQueue<BlockInfo> blockQueue;

        long blockCount;

        private void QueueBlock(BlockInfo block)
        {
            Interlocked.Increment(ref blockCount);

            if (oreBlocks.Contains(block.Name))
            {
                blockQueue.Enqueue(block);
            }
        }
    }

    public enum MinecraftEdition { Bedrock = 0, Java = 1 }

    public sealed class EnumConverter : IValueConverter
    {
        public static EnumConverter Instance { get; } = new EnumConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            if (targetType.IsEnum)
            {
                // convert int to enum
                return Enum.ToObject(targetType, value);
            }

            if (value.GetType().IsEnum)
            {
                // convert enum to int
                return System.Convert.ChangeType(
                    value,
                    Enum.GetUnderlyingType(value.GetType()));
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // perform the same conversion in both directions
            return Convert(value, targetType, parameter, culture);
        }
    }
}
