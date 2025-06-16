using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore.Metadata;
using ShowStasher.MVVM.Views;
using ShowStasher.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace ShowStasher.MVVM.ViewModels
{
    public partial class TmdbApiKeyViewModel : ObservableObject
    {
        private readonly SqliteDbService _dbService;
        private readonly Window _window;

        [ObservableProperty]
        private string apiKey = "";

        public TmdbApiKeyViewModel(SqliteDbService dbService, Window window)
        {
            _dbService = dbService;
            _window = window;
            _ = LoadApiKeyAsync();
        }

        private async Task LoadApiKeyAsync()
        {
            ApiKey = await _dbService.GetSettingAsync("TMDbApiKey") ?? "";
        }

        [RelayCommand]
        private async Task Save()
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                MessageBox.Show("API key cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate the API key before saving
            if (!await IsApiKeyValid(ApiKey))
            {
                MessageBox.Show("API key is invalid. Please check and try again.", "Invalid Key", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                await _dbService.SaveOrUpdateSettingAsync("TMDbApiKey", ApiKey);
                MessageBox.Show("API key saved successfully 🎉", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                // Close the dialog (optional — depends on how the window is opened)
                if (Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is TmdbApiKeyWindow) is Window window)
                    window.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save API key:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async Task<bool> IsApiKeyValid(string apiKey)
        {
            try
            {
                using var httpClient = new HttpClient();
                var url = $"https://api.themoviedb.org/3/movie/550?api_key={apiKey}";
                var response = await httpClient.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

    }
}
