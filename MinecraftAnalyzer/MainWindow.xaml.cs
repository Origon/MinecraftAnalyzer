using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using MiNET.LevelDB;
using NUnit.Framework;
using CsvHelper;
using System.Globalization;
using Microsoft.Win32;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace MinecraftAnalyzer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
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

        private void B_BrowseForImput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "Minecraft Bedrock World (*.mcworld)|*.mcworld";

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
            await AnalyzeChunksAroundCenterAsync(20, true);
            s.Stop();
            MessageBox.Show($"Completed after {Math.Floor(s.Elapsed.TotalMinutes)} minute(s) and {s.Elapsed.Seconds} second(s).");
            Running = false;
        }

        public void AnalyzeChunksAroundCenter(int chunksRadiusToCheck, bool multiThread = true)
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

                BlockQueue = new ConcurrentQueue<BlockInfo>();

                var chunkTasks = new List<Task>();

                for (int x = 0; x < chunksRadiusToCheck; x++)
                {
                    for (int z = 0; z < chunksRadiusToCheck; z++)
                    {
                        if (multiThread)
                        {
                            chunkTasks.Add(Analyzer.ParseChunkIfExistsAsync(db, new ChunkCoordinate(x, z), QueueBlock));
                        }
                        else
                        {
                            Analyzer.ParseChunkIfExists(db, new ChunkCoordinate(x, z), QueueBlock);
                        }                        

                        if (z > 0) 
                        {
                            if (multiThread)
                            {
                                chunkTasks.Add(Analyzer.ParseChunkIfExistsAsync(db, new ChunkCoordinate(x, z * -1), QueueBlock));
                            }
                            else
                            {
                                Analyzer.ParseChunkIfExists(db, new ChunkCoordinate(x, z * -1), QueueBlock);
                            }                                
                        }

                        if (x > 0)
                        {
                            if (multiThread)
                            {
                                chunkTasks.Add(Analyzer.ParseChunkIfExistsAsync(db, new ChunkCoordinate(x * -1, z), QueueBlock));
                            }
                            else
                            {
                                Analyzer.ParseChunkIfExists(db, new ChunkCoordinate(x * -1, z), QueueBlock);
                            }
                        }

                        if (x > 0 || z > 00)
                        {
                            if (multiThread)
                            {
                                chunkTasks.Add(Analyzer.ParseChunkIfExistsAsync(db, new ChunkCoordinate(x * -1, z * -1), QueueBlock));
                            }
                            else
                            {
                                Analyzer.ParseChunkIfExists(db, new ChunkCoordinate(x * -1, z * -1), QueueBlock);
                            }
                        }
                    }
                }

                Task.WaitAll(chunkTasks.ToArray());

                csvWriter.WriteRecords(BlockQueue);
            }
        }

        public Task AnalyzeChunksAroundCenterAsync(int chunksRadiusToCheck, bool multiThread = true)
        {
            return Task.Run(() => AnalyzeChunksAroundCenter(chunksRadiusToCheck, multiThread));
        }

        public class BlockInfoCsvMap : CsvHelper.Configuration.ClassMap<BlockInfo>
        {
            public BlockInfoCsvMap()
            {
                Map(b => b.Coordinates, false).Name("X").ConvertUsing(row => row.Coordinates.X.ToString());
                Map(b => b.Coordinates, false).Name("Y").ConvertUsing(row => row.Coordinates.Y.ToString());
                Map(b => b.Coordinates, false).Name("Z").ConvertUsing(row => row.Coordinates.Z.ToString());
                Map(b => b.State, false).Name("Block").ConvertUsing(row => row.State.RootTag["name"].StringValue);
            }
        }

        private string[] oreBlocks = { "minecraft:gold_ore",
                                       "minecraft:iron_ore",
                                       "minecraft:coal_ore",
                                       "minecraft:lapis_ore",
                                       "minecraft:diamond_ore",
                                       "minecraft:redstone_ore",
                                       "minecraft:lit_redstone_ore",
                                       "minecraft:emerald_ore",
                                       "minecraft:lava",
                                       "minecraft:flowing_lava"};

        private ConcurrentQueue<BlockInfo> BlockQueue;

        private void QueueBlock(BlockInfo block)
        {
            if (oreBlocks.Contains(block.State.RootTag["name"].StringValue))
            {
                BlockQueue.Enqueue(block);
            }
        }
    }
}
