using Microsoft.Win32;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace P4G_PC_Music_Converter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string AdpcmEncoderPath = "tools/AdpcmEncode.exe";

        public MainWindow()
        {
            InitializeComponent();
        }
        public static bool batchConvert = false;
        private void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            // Check to ensure all the necessary fields are filled out before we proceed
            bool fieldsPopulated = !string.IsNullOrWhiteSpace(InputWavPath.Text) &&
                !string.IsNullOrWhiteSpace(OutputRawPath.Text) &&
                (!LoopEnable.IsChecked.Value || (!string.IsNullOrWhiteSpace(LoopStart.Text)) && !string.IsNullOrWhiteSpace(LoopEnd.Text));

            if (!fieldsPopulated && FullLoop.IsChecked == false)
            {
                MessageBox.Show("Not all required fields have been filled out!\n" +
                    "Make sure that both input and output file paths are specified,\n" +
                    "and if looping is enabled, make sure the start and end point are provided.");
                return;
            }

            if (batchConvert)
            {
                foreach (string file in Directory.GetFiles(InputWavPath.Text,"*.wav"))
                {
                    if (!Directory.Exists(OutputRawPath.Text))
                        Directory.CreateDirectory(OutputRawPath.Text);
                    // Verify that the input file exists
                    if (!File.Exists(file))
                    {
                        MessageBox.Show("The specified input file does not exist!");
                        return;
                    }
                    if (File.Exists(OutputRawPath.Text + "\\" +Path.GetFileNameWithoutExtension(file) + ".raw"))
                    {
                        File.Delete(OutputRawPath.Text + "\\" + Path.GetFileNameWithoutExtension(file) + ".raw");
                    }
                    if (File.Exists(OutputRawPath.Text + "\\" + Path.GetFileNameWithoutExtension(file) + ".raw.txth"))
                    {
                        File.Delete(OutputRawPath.Text + "\\" + Path.GetFileNameWithoutExtension(file) + ".raw.txth");
                    }
                    // If looping is enabled, ensure that the loop start and end textboxes contain valid numbers
                    if (LoopEnable.IsChecked.Value && FullLoop.IsChecked == false)
                    {
                        if (!int.TryParse(LoopStart.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int _) ||
                            int.Parse(LoopStart.Text, NumberStyles.Integer) < 0)
                        {
                            MessageBox.Show("Unable to parse the loop start point, make sure it's a valid positive integer!");
                            return;
                        }

                        if (!int.TryParse(LoopEnd.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int _) ||
                            int.Parse(LoopEnd.Text, NumberStyles.Integer) < 0)
                        {
                            MessageBox.Show("Unable to parse the loop end point, make sure it's a valid positive integer!");
                            return;
                        }
                    }

                    string encodedInputPath2;
                    if (!EncodingPassthrough.IsChecked.Value)
                    {
                        encodedInputPath2 = OutputRawPath.Text + "\\" + Path.GetFileNameWithoutExtension(file) + ".raw";

                        // Encode the input file using the MSADPCM tool
                        if (!File.Exists(AdpcmEncoderPath))
                        {
                            MessageBox.Show($"Unable to find MSADPCM encoder tool! It should be located in {AdpcmEncoderPath}");
                            return;
                        }

                        ProcessStartInfo startInfo = new ProcessStartInfo(AdpcmEncoderPath, $"\"{file}\" \"{OutputRawPath.Text}\\{Path.GetFileNameWithoutExtension(file)}.raw\"");
                        var encodeProcess = Process.Start(startInfo);
                        encodeProcess.WaitForExit();
                        int exit = encodeProcess.ExitCode;

                        if (exit != 0)
                        {
                            MessageBox.Show($"The MSADPCM encoder tool exited with code {exit}. This indicates an error. Aborting...");
                            return;
                        }
                    }
                    else
                    {
                        encodedInputPath2 = file;
                    }

                    // Read the input file and determine/verify its info (sample count, etc.)
                    byte[] dataSegment2;
                    using (WaveFileReader waveReader = new WaveFileReader(encodedInputPath2))
                    {
                        // Store the data segment (what ends up in the final RAW file) in a byte array temporarily
                        dataSegment2 = new byte[waveReader.Length];
                        waveReader.Read(dataSegment2, 0, (int)waveReader.Length);

                        // Create a StringBuilder to output the info about this file to our TextBlock
                        StringBuilder outputInfoBuilder = new StringBuilder();

                        // We can't get the sample count from the ADPCM encoded wave, so we need to use the original input file
                        long sampleCount = -1;
                        using (WaveFileReader originalWaveReader = new WaveFileReader(file))
                        {
                            sampleCount = originalWaveReader.SampleCount;
                        }

                        outputInfoBuilder.Append($"num_samples = {sampleCount}\n");

                        string encodingString;
                        if (waveReader.WaveFormat.Encoding.ToString().ToUpperInvariant() == "ADPCM")
                        {
                            encodingString = "MSADPCM";
                        }
                        else if (waveReader.WaveFormat.Encoding.ToString().ToUpperInvariant() == "PCM")
                        {
                            encodingString = waveReader.WaveFormat.Encoding.ToString().ToUpperInvariant() + waveReader.WaveFormat.BitsPerSample.ToString();
                            if (waveReader.WaveFormat.BitsPerSample == 16)
                            {
                                encodingString += "LE";
                            }

                            if (waveReader.WaveFormat.BitsPerSample > 16)
                            {
                                MessageBox.Show($"The provided input file uses an unsupported PCM encoding: {encodingString}");
                                return;
                            }
                        }
                        else
                        {
                            MessageBox.Show($"The provided input file uses an unsupported codec: {waveReader.WaveFormat.Encoding.ToString().ToUpperInvariant()}");
                            return;
                        }
                        outputInfoBuilder.Append($"codec = {encodingString}\n");

                        outputInfoBuilder.Append($"channels = {waveReader.WaveFormat.Channels}\n");
                        outputInfoBuilder.Append($"sample_rate = {waveReader.WaveFormat.SampleRate}\n");
                        outputInfoBuilder.Append($"interleave = {waveReader.WaveFormat.BlockAlign}\n");


                        // Verify and adjust the loop points if needed, to conform to block sample alignment
                        if (LoopEnable.IsChecked.Value && FullLoop.IsChecked == false)
                        {
                            int samplesPerBlock = waveReader.WaveFormat.BlockAlign - (6 * waveReader.WaveFormat.Channels);

                            int loopStart = int.Parse(LoopStart.Text);
                            //loopStart = (loopStart % samplesPerBlock) != 0 ? (loopStart + samplesPerBlock - (loopStart % samplesPerBlock)) : loopStart;
                            if (loopStart % samplesPerBlock != 0 && !EncodingPassthrough.IsChecked.Value)
                            {
                                switch (MessageBox.Show($"The provided loop start point is not aligned to {samplesPerBlock} samples per block, would you like to adjust the loop point?",
                                    string.Empty, MessageBoxButton.YesNoCancel))
                                {
                                    case MessageBoxResult.Yes:
                                        loopStart += samplesPerBlock - (loopStart % samplesPerBlock);
                                        if (loopStart > sampleCount)
                                            loopStart -= samplesPerBlock;
                                        if (loopStart < 0)
                                            loopStart = int.Parse(LoopStart.Text);  // If all else fails just use what we're told
                                        break;

                                    case MessageBoxResult.No:
                                        break;

                                    case MessageBoxResult.Cancel:
                                        return;
                                }
                            }

                            if (loopStart > sampleCount || loopStart < 0)
                            {
                                MessageBox.Show("The loop start point is out of bounds.");
                                return;
                            }


                            int loopEnd = int.Parse(LoopEnd.Text);
                            //loopEnd = (loopEnd % samplesPerBlock) != 0 ? (loopEnd + samplesPerBlock - (loopEnd % samplesPerBlock)) : loopEnd;
                            if (loopEnd % samplesPerBlock != 0 && !EncodingPassthrough.IsChecked.Value)
                            {
                                switch (MessageBox.Show($"The provided loop end point is not aligned to {samplesPerBlock} samples per block, would you like to adjust the loop point?",
                                    string.Empty, MessageBoxButton.YesNoCancel))
                                {
                                    case MessageBoxResult.Yes:
                                        loopEnd += samplesPerBlock - (loopEnd % samplesPerBlock);
                                        if (loopEnd > sampleCount)
                                            loopEnd -= samplesPerBlock;
                                        if (loopEnd < 0)
                                            loopEnd = int.Parse(LoopEnd.Text);  // If all else fails just use what we're told
                                        break;

                                    case MessageBoxResult.No:
                                        break;

                                    case MessageBoxResult.Cancel:
                                        return;
                                }
                            }

                            if (loopEnd > sampleCount || loopEnd < 0)
                            {
                                MessageBox.Show("The loop end point is out of bounds.");
                                return;
                            }

                            if (loopStart >= loopEnd)
                            {
                                MessageBox.Show("The loop start point would be equal to or after the end point. This is invalid.");
                                return;
                            }

                            outputInfoBuilder.Append($"loop_start_sample = {loopStart}\n");
                            outputInfoBuilder.Append($"loop_end_sample = {loopEnd}\n");
                        }

                        if (FullLoop.IsChecked == true)
                        {
                            outputInfoBuilder.Append($"loop_start_sample = 0\n");
                            outputInfoBuilder.Append($"loop_end_sample = {sampleCount}\n");
                        }
                        OutputFileInfo.Text = outputInfoBuilder.ToString();

                        // Setup an output TXTH file in the same location as the output RAW file
                        File.WriteAllText(OutputRawPath.Text + "\\" + Path.GetFileNameWithoutExtension(file) + ".raw.txth", outputInfoBuilder.ToString());
                    }

                    // Save only the data segment to our final RAW file
                    File.WriteAllBytes(OutputRawPath.Text + "\\" + Path.GetFileNameWithoutExtension(file) + ".raw", dataSegment2);
                    


                }
                MessageBox.Show("All files converted");
                return;
            }









            // Verify that the input file exists
            if (!File.Exists(InputWavPath.Text))
            {
                MessageBox.Show("The specified input file does not exist!");
                return;
            }

            // If looping is enabled, ensure that the loop start and end textboxes contain valid numbers
            if (LoopEnable.IsChecked.Value)
            {
                if (!int.TryParse(LoopStart.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int _) ||
                    int.Parse(LoopStart.Text, NumberStyles.Integer) < 0)
                {
                    MessageBox.Show("Unable to parse the loop start point, make sure it's a valid positive integer!");
                    return;
                }

                if (!int.TryParse(LoopEnd.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int _) ||
                    int.Parse(LoopEnd.Text, NumberStyles.Integer) < 0)
                {
                    MessageBox.Show("Unable to parse the loop end point, make sure it's a valid positive integer!");
                    return;
                }
            }

            string encodedInputPath;
            if (!EncodingPassthrough.IsChecked.Value)
            {
                encodedInputPath = OutputRawPath.Text;

                // Encode the input file using the MSADPCM tool
                if (!File.Exists(AdpcmEncoderPath))
                {
                    MessageBox.Show($"Unable to find MSADPCM encoder tool! It should be located in {AdpcmEncoderPath}");
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo(AdpcmEncoderPath, $"\"{InputWavPath.Text}\" \"{OutputRawPath.Text}\"");
                var encodeProcess = Process.Start(startInfo);
                encodeProcess.WaitForExit();
                int exit = encodeProcess.ExitCode;

                if (exit != 0)
                {
                    MessageBox.Show($"The MSADPCM encoder tool exited with code {exit}. This indicates an error. Aborting...");
                    return;
                }
            }
            else
            {
                encodedInputPath = InputWavPath.Text;
            }

            // Read the input file and determine/verify its info (sample count, etc.)
            byte[] dataSegment;
            using (WaveFileReader waveReader = new WaveFileReader(encodedInputPath))
            {
                // Store the data segment (what ends up in the final RAW file) in a byte array temporarily
                dataSegment = new byte[waveReader.Length];
                waveReader.Read(dataSegment, 0, (int)waveReader.Length);

                // Create a StringBuilder to output the info about this file to our TextBlock
                StringBuilder outputInfoBuilder = new StringBuilder();

                // We can't get the sample count from the ADPCM encoded wave, so we need to use the original input file
                long sampleCount = -1;
                using (WaveFileReader originalWaveReader = new WaveFileReader(InputWavPath.Text))
                {
                    sampleCount = originalWaveReader.SampleCount;
                }

                outputInfoBuilder.Append($"num_samples = {sampleCount}\n");

                string encodingString;
                if (waveReader.WaveFormat.Encoding.ToString().ToUpperInvariant() == "ADPCM")
                {
                    encodingString = "MSADPCM";
                }
                else if (waveReader.WaveFormat.Encoding.ToString().ToUpperInvariant() == "PCM")
                {
                    encodingString = waveReader.WaveFormat.Encoding.ToString().ToUpperInvariant() + waveReader.WaveFormat.BitsPerSample.ToString();
                    if (waveReader.WaveFormat.BitsPerSample == 16)
                    {
                        encodingString += "LE";
                    }

                    if (waveReader.WaveFormat.BitsPerSample > 16)
                    {
                        MessageBox.Show($"The provided input file uses an unsupported PCM encoding: {encodingString}");
                        return;
                    }
                }
                else
                {
                    MessageBox.Show($"The provided input file uses an unsupported codec: {waveReader.WaveFormat.Encoding.ToString().ToUpperInvariant()}");
                    return;
                }
                outputInfoBuilder.Append($"codec = {encodingString}\n");

                outputInfoBuilder.Append($"channels = {waveReader.WaveFormat.Channels}\n");
                outputInfoBuilder.Append($"sample_rate = {waveReader.WaveFormat.SampleRate}\n");
                outputInfoBuilder.Append($"interleave = {waveReader.WaveFormat.BlockAlign}\n");


                // Verify and adjust the loop points if needed, to conform to block sample alignment
                if (LoopEnable.IsChecked.Value)
                {
                    int samplesPerBlock = waveReader.WaveFormat.BlockAlign - (6 * waveReader.WaveFormat.Channels);

                    int loopStart = int.Parse(LoopStart.Text);
                    //loopStart = (loopStart % samplesPerBlock) != 0 ? (loopStart + samplesPerBlock - (loopStart % samplesPerBlock)) : loopStart;
                    if (loopStart % samplesPerBlock != 0 && !EncodingPassthrough.IsChecked.Value)
                    {
                        switch (MessageBox.Show($"The provided loop start point is not aligned to {samplesPerBlock} samples per block, would you like to adjust the loop point?",
                            string.Empty, MessageBoxButton.YesNoCancel))
                        {
                            case MessageBoxResult.Yes:
                                loopStart += samplesPerBlock - (loopStart % samplesPerBlock);
                                if (loopStart > sampleCount)
                                    loopStart -= samplesPerBlock;
                                if (loopStart < 0)
                                    loopStart = int.Parse(LoopStart.Text);  // If all else fails just use what we're told
                                break;

                            case MessageBoxResult.No:
                                break;

                            case MessageBoxResult.Cancel:
                                return;
                        }
                    }

                    if (loopStart > sampleCount || loopStart < 0)
                    {
                        MessageBox.Show("The loop start point is out of bounds.");
                        return;
                    }


                    int loopEnd = int.Parse(LoopEnd.Text);
                    //loopEnd = (loopEnd % samplesPerBlock) != 0 ? (loopEnd + samplesPerBlock - (loopEnd % samplesPerBlock)) : loopEnd;
                    if (loopEnd % samplesPerBlock != 0 && !EncodingPassthrough.IsChecked.Value)
                    {
                        switch (MessageBox.Show($"The provided loop end point is not aligned to {samplesPerBlock} samples per block, would you like to adjust the loop point?",
                            string.Empty, MessageBoxButton.YesNoCancel))
                        {
                            case MessageBoxResult.Yes:
                                loopEnd += samplesPerBlock - (loopEnd % samplesPerBlock);
                                if (loopEnd > sampleCount)
                                    loopEnd -= samplesPerBlock;
                                if (loopEnd < 0)
                                    loopEnd = int.Parse(LoopEnd.Text);  // If all else fails just use what we're told
                                break;

                            case MessageBoxResult.No:
                                break;

                            case MessageBoxResult.Cancel:
                                return;
                        }
                    }

                    if (loopEnd > sampleCount || loopEnd < 0)
                    {
                        MessageBox.Show("The loop end point is out of bounds.");
                        return;
                    }

                    if (loopStart >= loopEnd)
                    {
                        MessageBox.Show("The loop start point would be equal to or after the end point. This is invalid.");
                        return;
                    }

                    outputInfoBuilder.Append($"loop_start_sample = {loopStart}\n");
                    outputInfoBuilder.Append($"loop_end_sample = {loopEnd}\n");
                }

                OutputFileInfo.Text = outputInfoBuilder.ToString();

                // Setup an output TXTH file in the same location as the output RAW file
                File.WriteAllText(OutputRawPath.Text + ".txth", outputInfoBuilder.ToString());
            }

            // Save only the data segment to our final RAW file
            File.WriteAllBytes(OutputRawPath.Text, dataSegment);
        }

        private void BrowseInputFileButton_Click(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog dlg = new CommonOpenFileDialog();
            if (batchConvert)
            {
                dlg.IsFolderPicker = true;
            }
            else
                dlg.Filters.Add(new CommonFileDialogFilter("Wav file", "*.wav"));

            // Abort if the user canceled the dialog
            if (dlg.ShowDialog() != CommonFileDialogResult.Ok)
                return;


            InputWavPath.Text = dlg.FileName;



            // If there is nothing in the output file path textbox, populate it with an automatic output file in the same location
            if (string.IsNullOrWhiteSpace(OutputRawPath.Text))
            {
                if (batchConvert)
                {
                    OutputRawPath.Text = dlg.FileName + "\\raw";
                }
                else
                {
                    FileInfo temp = new FileInfo(dlg.FileName);
                    OutputRawPath.Text = temp.FullName.Substring(0, temp.FullName.Length - temp.Extension.Length) + ".raw";
                }
                
            }
        }

        private void BrowseOutputFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (batchConvert)
            {
                CommonOpenFileDialog dlg = new CommonOpenFileDialog();
                if (batchConvert)
                {
                    dlg.IsFolderPicker = true;
                }
                if (dlg.ShowDialog() != CommonFileDialogResult.Ok)
                    return;
                OutputRawPath.Text = dlg.FileName;
            }
            else
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Filter = "RAW audio files (*.raw)|*.raw|All files (*.*)|*.*";

                // Abort if the user canceled the dialog
                if (!dlg.ShowDialog().Value)
                {
                    return;
                }

                OutputRawPath.Text = dlg.FileName;
            }

        }

        private void LoopEnable_Checked(object sender, RoutedEventArgs e)
        {
            LoopStart.IsEnabled = true;
            LoopEnd.IsEnabled = true;
            FullLoop.IsEnabled = true;
        }

        private void LoopEnable_Unchecked(object sender, RoutedEventArgs e)
        {
            FullLoop.IsChecked = false;
            LoopStart.IsEnabled = false;
            LoopEnd.IsEnabled = false;
            FullLoop.IsEnabled = false;
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow about = new AboutWindow();
            about.ShowDialog();
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (batchConvert)
            {
                bruh.Content = "Input WAV file:";
                bruh2.Content = "Output RAW file:";
                batchConvert = false;
            }
            else
            {
                bruh.Content = "Input Directory:";
                bruh2.Content = "Output Directory:";
                batchConvert = true;
            }

        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (batchConvert)
            {
                bruh.Content = "Input WAV file:";
                bruh2.Content = "Output RAW file:";
                batchConvert = false;
            }
            else
            {
                bruh.Content = "Input Directory:";
                bruh2.Content = "Output Directory:";
                batchConvert = true;
            }
        }

        private void FullLoop_Checked(object sender, RoutedEventArgs e)
        {
            LoopStart.IsEnabled = false;
            LoopEnd.IsEnabled = false;
        }

        private void FullLoop_Unchecked(object sender, RoutedEventArgs e)
        {
            LoopStart.IsEnabled = true;
            LoopEnd.IsEnabled = true;
        }
    }
}

