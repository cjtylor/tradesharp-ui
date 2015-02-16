﻿using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using TradeHub.Common.Core.Constants;
using TradeHubGui.Common;
using TradeHubGui.Common.Models;

namespace TradeHubGui.ViewModel
{
    public class DashboardViewModel : BaseViewModel
    {
        private ObservableCollection<Instrument> _instruments;
        private RelayCommand _showDataApiConfigurationCommand;
        private RelayCommand _showOrderApiConfigurationCommand;
        private ProvidersViewModel providersViewModel;

        /// <summary>
        /// Constructors
        /// </summary>
        public DashboardViewModel()
        {
            providersViewModel = new ProvidersViewModel();

            #region Temporary fill instruments (this will be removed)
            _instruments = new ObservableCollection<Instrument>();
            for (int i = 0; i < 10; i++)
            {
                _instruments.Add(new Instrument("AAPL", 23, 450.34f, 20, 456.00f, 445.34f, 23));
                _instruments.Add(new Instrument("GOOG", 43, 450.34f, 20, 456.00f, 445.34f, 23));
                _instruments.Add(new Instrument("MSFT", 33, 450.34f, 20, 456.00f, 445.34f, 23));
                _instruments.Add(new Instrument("HP", 42, 450.34f, 20, 456.00f, 445.34f, 23));
                _instruments.Add(new Instrument("AOI", 34, 450.34f, 20, 456.00f, 445.34f, 23));
                _instruments.Add(new Instrument("WAS", 15, 450.34f, 20, 456.00f, 445.34f, 23));
                _instruments.Add(new Instrument("AAPL", 23, 450.34f, 20, 456.00f, 445.34f, 23));
                _instruments.Add(new Instrument("GOOG", 23, 450.34f, 20, 456.00f, 445.34f, 23));
                _instruments.Add(new Instrument("MSFT", 45, 450.34f, 20, 456.00f, 445.34f, 23));
                _instruments.Add(new Instrument("HP", 33, 450.34f, 20, 456.00f, 445.34f, 23));
                _instruments.Add(new Instrument("AOI", 24, 450.34f, 20, 456.00f, 445.34f, 23));
                _instruments.Add(new Instrument("WAS", 23, 450.34f, 20, 456.00f, 445.34f, 23));
            }
            #endregion
        }

        #region Properties

        public ObservableCollection<Instrument> Instruments
        {
            get { return _instruments; }
            set
            {
                _instruments = value;
                OnPropertyChanged("Instruments");
            }
        }

        /// <summary>
        /// Collection of market data providers for displaying on Dashboard
        /// </summary>
        public ObservableCollection<Provider> MarketDataProviders
        {
            get { return providersViewModel.MarketDataProviders; }
        }

        /// <summary>
        /// Collection of order execution providers for displaying on Dashboard
        /// </summary>
        public ObservableCollection<Provider> OrderExecutionProviders
        {
            get { return providersViewModel.OrderExecutionProviders; }
        }

        #endregion

        #region Commands

        public ICommand ShowDataApiConfigurationCommand
        {
            get
            {
                return _showDataApiConfigurationCommand ?? (_showDataApiConfigurationCommand = new RelayCommand(param => ShowDataApiConfigurationExecute()));
            }
        }

        public ICommand ShowOrderApiConfigurationCommand
        {
            get
            {
                return _showOrderApiConfigurationCommand ?? (_showOrderApiConfigurationCommand = new RelayCommand(param => ShowOrderApiConfigurationExecute()));
            }
        }

        #endregion

        private void ShowDataApiConfigurationExecute()
        {
            if(ToggleFlyout(0))
            {
                (MainWindow.Flyouts.Items[0] as Flyout).DataContext = providersViewModel;
            }
        }

        private object GetMarketDataProviders()
        {
            throw new NotImplementedException();
        }

        private void ShowOrderApiConfigurationExecute()
        {
            if (ToggleFlyout(1))
            {
                (MainWindow.Flyouts.Items[1] as Flyout).DataContext = providersViewModel;
            }
        }
    }
}
