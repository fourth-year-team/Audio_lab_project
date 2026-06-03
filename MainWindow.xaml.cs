using System;
using System.Windows;
using Microsoft.Win32;
using AudioLabProject.ViewModels;

namespace AudioLabProject
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DragEnter += MainWindow_DragEnter;
            DragLeave += MainWindow_DragLeave;
            DragOver += MainWindow_DragOver;
        }

        private void MainWindow_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void MainWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                var viewModel = (MainViewModel)DataContext;
                viewModel.IsDraggingOver = true;
            }
        }

        private void MainWindow_DragLeave(object sender, DragEventArgs e)
        {
            var viewModel = (MainViewModel)DataContext;
            viewModel.IsDraggingOver = false;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            var viewModel = (MainViewModel)DataContext;
            viewModel.IsDraggingOver = false;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    await viewModel.LoadFileAsync(files[0]);
                }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (MainViewModel)DataContext;
            if (viewModel.ProcessedSamples == null)
            {
                MessageBox.Show("Please process an audio file first.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "WAV Files (*.wav)|*.wav",
                DefaultExt = "wav"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                viewModel.SaveProcessed(saveFileDialog.FileName);
            }
        }

    }
}
