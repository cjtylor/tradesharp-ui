﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Disruptor;
using TraceSourceLogger;
using TradeHub.Common.Core.Constants;
using TradeHub.Common.Core.DomainModels;
using TradeHub.Common.Core.DomainModels.OrderDomain;
using TradeHub.Common.HistoricalDataProvider.Service;
using TradeHub.Common.HistoricalDataProvider.Utility;
using TradeHub.Common.HistoricalDataProvider.ValueObjects;
using TradeHub.Common.Persistence;
using TradeHub.StrategyEngine.TradeHub;
using TradeHub.StrategyEngine.Utlility.Services;

namespace TradeHubGui.StrategyRunner.Services
{
    /// <summary>
    /// Responsibe for handling individual strategy instances
    /// </summary>
    public class StrategyExecutor
    {
        private Type _type = typeof(StrategyExecutor);
        private AsyncClassLogger _asyncClassLogger;

        /// <summary>
        /// Responsible for providing order executions in backtesting
        /// </summary>
        private OrderExecutor _orderExecutor;

        /// <summary>
        /// Manages order requests from strategy in backtesting
        /// </summary>
        private OrderRequestListener _orderRequestListener;

        /// <summary>
        /// Manages market data for backtesting strategy
        /// </summary>
        private MarketDataListener _marketDataListener;

        /// <summary>
        /// Manages market data requests from strategy in backtesting
        /// </summary>
        private MarketRequestListener _marketRequestListener;

        /// <summary>
        /// Responsible for providing requested data
        /// </summary>
        private DataHandler _dataHandler;

        /// <summary>
        /// Unique Key to Identify the Strategy Instance
        /// </summary>
        private string _strategyKey;

        /// <summary>
        /// Indicates whether the strategy is None/Executing/Executed
        /// </summary>
        private string _strategyStatus;

        /// <summary>
        /// Save Custom Strategy Type (C# Class Type which implements TradeHubStrategy.cs)
        /// </summary>
        private Type _strategyType;

        /// <summary>
        /// Holds reference of Strategy Instance
        /// </summary>
        private TradeHubStrategy _tradeHubStrategy;

        private Bar _currentBar;
        private Bar _prevBar;

        /// <summary>
        /// Holds selected ctor arguments to execute strategy
        /// </summary>
        private object[] _ctorArguments;

        #region Events

        // ReSharper disable InconsistentNaming
        private event Action<bool, string> _statusChanged;
        private event Action<Execution> _executionReceived;
        // ReSharper restore InconsistentNaming

        /// <summary>
        /// Raised when custom strategy status changed from Running-to-Stopped and vice versa
        /// </summary>
        public event Action<bool, string> StatusChanged
        {
            add { if (_statusChanged == null) _statusChanged += value; }
            remove { _statusChanged -= value; }
        }

        /// <summary>
        /// Raised when new execution is received by the custom strategy
        /// </summary>
        public event Action<Execution> ExecutionReceived
        {
            add { if (_executionReceived == null) _executionReceived += value; }
            remove { _executionReceived -= value; }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Unique Key to Identify the Strategy Instance
        /// </summary>
        public string StrategyKey
        {
            get { return _strategyKey; }
            set { _strategyKey = value; }
        }

        /// <summary>
        /// Holds selected ctor arguments to execute strategy
        /// </summary>
        public object[] CtorArguments
        {
            get { return _ctorArguments; }
            set { _ctorArguments = value; }
        }

        /// <summary>
        /// Indicates whether the strategy is None/Executing/Executed
        /// </summary>
        public string StrategyStatus
        {
            get { return _strategyStatus; }
            set { _strategyStatus = value; }
        }

        #endregion

        /// <summary>
        /// Argument Constructor
        /// </summary>
        /// <param name="strategyKey">Unique Key to Identify the Strategy Instance</param>
        /// <param name="strategyType">C# Class Type which implements TradeHubStrategy.cs</param>
        /// <param name="ctorArguments">Holds selected ctor arguments to execute strategy</param>
        public StrategyExecutor(string strategyKey, Type strategyType, object[] ctorArguments)
        {
            //_asyncClassLogger = ContextRegistry.GetContext()["StrategyRunnerLogger"] as AsyncClassLogger;
            _asyncClassLogger = new AsyncClassLogger("StrategyExecutor");
            {
                _asyncClassLogger.SetLoggingLevel();
                //set logging path
                string path = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) +
                                  "\\TradeHub Logs\\Client";
                _asyncClassLogger.LogDirectory(path);
            }

            _tradeHubStrategy = null;
            _strategyKey = strategyKey;
            _strategyType = strategyType;
            _ctorArguments = ctorArguments;
            //TODO: Assign status
            //_strategyStatus = Infrastructure.Constants.StrategyStatus.None;

            // Initialze Utility Classes
            _orderExecutor = new OrderExecutor(_asyncClassLogger);
            _marketDataListener = new MarketDataListener(_asyncClassLogger);
            _orderRequestListener = new OrderRequestListener(_orderExecutor, _asyncClassLogger);

            // Use MarketDataListener.cs as Event Handler to get data from DataHandler.cs
            _dataHandler =new DataHandler(new IEventHandler<MarketDataObject>[] { _marketDataListener });

            _marketDataListener.BarSubscriptionList = _dataHandler.BarSubscriptionList;
            _marketDataListener.TickSubscriptionList = _dataHandler.TickSubscriptionList;

            // Initialize MarketRequestListener.cs to manage incoming market data requests from strategy
            _marketRequestListener = new MarketRequestListener(_dataHandler, _asyncClassLogger);

            //Register OrderExecutor Events
            RegisterOrderExecutorEvents();

            //Register Market Data Listener Events
            RegisterMarketDataListenerEvents();
        }

        /// <summary>
        /// Starts custom strategy execution
        /// </summary>
        public void ExecuteStrategy()
        {
            try
            {
                // Verify Strategy Instance
                if (_tradeHubStrategy == null)
                {
                    //create DB strategy 
                    Strategy strategy = new Strategy();
                    strategy.Name = _strategyType.Name;
                    strategy.StartDateTime = DateTime.Now;

                    // Get new strategy instance
                    var strategyInstance = StrategyHelper.CreateStrategyInstance(_strategyType, CtorArguments);

                    if (strategyInstance != null)
                    {
                        // Cast to TradeHubStrategy Instance
                        _tradeHubStrategy = strategyInstance as TradeHubStrategy;
                    }

                    if (_tradeHubStrategy == null)
                    {
                        if (_asyncClassLogger.IsInfoEnabled)
                        {
                            _asyncClassLogger.Info("Unable to initialize Custom Strategy: " + _strategyType.FullName, _type.FullName, "ExecuteStrategy");
                        }

                        // Skip execution of further actions
                        return;
                    }

                    // Set Strategy Name
                    _tradeHubStrategy.StrategyName = StrategyHelper.GetCustomClassSummary(_strategyType);

                    // Register Events
                    _tradeHubStrategy.OnStrategyStatusChanged += OnStrategyStatusChanged;
                    _tradeHubStrategy.OnNewExecutionReceived += OnNewExecutionReceived;
                }

                if (_asyncClassLogger.IsInfoEnabled)
                {
                    _asyncClassLogger.Info("Executing user strategy: " + _strategyType.FullName, _type.FullName, "ExecuteStrategy");
                }

                //Overriding if running on simulated exchange
                ManageBackTestingStrategy();

                // Start Executing the strategy
                _tradeHubStrategy.Run();
            }
            catch (Exception exception)
            {
                _asyncClassLogger.Error(exception, _type.FullName, "ExecuteStrategy");
            }
        }

        /// <summary>
        /// Stops custom strategy execution
        /// </summary>
        public void StopStrategy()
        {
            try
            {
                // Verify Strategy Instance
                if (_tradeHubStrategy != null)
                {
                    if (_asyncClassLogger.IsInfoEnabled)
                    {
                        _asyncClassLogger.Info("Stopping user strategy execution: " + _strategyType.FullName, _type.FullName, "StopStrategy");
                    }

                    // Start Executing the strategy
                    _tradeHubStrategy.Stop();
                }
                else
                {
                    if (_asyncClassLogger.IsInfoEnabled)
                    {
                        _asyncClassLogger.Info("User strategy not initialized: " + _strategyType.FullName, _type.FullName, "StopStrategy");
                    }
                }
            }
            catch (Exception exception)
            {
                _asyncClassLogger.Error(exception, _type.FullName, "StopStrategy");
            }
        }

        #region Manage Back-Testing Strategy (i.e. Provider = SimulatedExchange)

        /// <summary>
        /// Will take appropariate actions to handle a strategy intended to be back tested
        /// </summary>
        private void ManageBackTestingStrategy()
        {
            if (_tradeHubStrategy != null)
            {
                if (_tradeHubStrategy.MarketDataProviderName.Equals(MarketDataProvider.SimulatedExchange))
                    OverrideStrategyDataEvents();
                if (_tradeHubStrategy.OrderExecutionProviderName.Equals(OrderExecutionProvider.SimulatedExchange))
                    OverrideStrategyOrderRequests();
            }
        }

        /// <summary>
        /// Overrides required data events for backtesting strategy
        /// </summary>
        private void OverrideStrategyDataEvents()
        {
            //NOTE: LOCAL Data

            _tradeHubStrategy.OverrideTickSubscriptionRequest(_marketRequestListener.SubscribeTickData);
            _tradeHubStrategy.OverrideTickUnsubscriptionRequest(_marketRequestListener.UnsubscribeTickData);

            _tradeHubStrategy.OverrideBarSubscriptionRequest(_marketRequestListener.SubscribeLiveBars);
            _tradeHubStrategy.OverriderBarUnsubscriptionRequest(_marketRequestListener.UnsubcribeLiveBars);

            ////NOTE: SX Data
            //_tradeHubStrategy.InitializeMarketDataServiceDisruptor(new IEventHandler<RabbitMqMessage>[] { _marketDataListener });
        }

        /// <summary>
        /// Overrides backtesting strategy's order requests to manage them inside strategy runner
        /// </summary>
        private void OverrideStrategyOrderRequests()
        {
            _tradeHubStrategy.OverrideMarketOrderRequest(_orderRequestListener.NewMarketOrderRequest);
            _tradeHubStrategy.OverrideLimitOrderRequest(_orderRequestListener.NewLimitOrderRequest);
            _tradeHubStrategy.OverrideCancelOrderRequest(_orderRequestListener.NewCancelOrderRequest);
        }

        /// <summary>
        /// Subscribes order events from <see cref="OrderExecutor"/>
        /// </summary>
        private void RegisterOrderExecutorEvents()
        {
            _orderExecutor.NewOrderArrived += OnOrderExecutorNewArrived;
            _orderExecutor.ExecutionArrived += OnOrderExecutorExecutionArrived;
            _orderExecutor.RejectionArrived += OnOrderExecutorRejectionArrived;
            _orderExecutor.CancellationArrived += OnOrderExecutorCancellationArrived;
        }

        /// <summary>
        /// Subscribes Tick and Bars events from <see cref="MarketDataListener"/>
        ///  </summary>
        private void RegisterMarketDataListenerEvents()
        {
            _marketDataListener.TickArrived += OnTickArrived;
            _marketDataListener.BarArrived += OnBarArrived;
        }

        /// <summary>
        /// Called when Cancellation received from <see cref="OrderExecutor"/>
        /// </summary>
        /// <param name="order"></param>
        private void OnOrderExecutorCancellationArrived(Order order)
        {
            _tradeHubStrategy.OnCancellationArrived(order);
            PersistencePublisher.PublishDataForPersistence(order);
        }

        /// <summary>
        /// Called when Rejection received from <see cref="OrderExecutor"/>
        /// </summary>
        /// <param name="rejection"></param>
        private void OnOrderExecutorRejectionArrived(Rejection rejection)
        {
            _tradeHubStrategy.OnRejectionArrived(rejection);
        }

        /// <summary>
        /// Called when Executions received from <see cref="OrderExecutor"/>
        /// </summary>
        /// <param name="execution"></param>
        private void OnOrderExecutorExecutionArrived(Execution execution)
        {
            _tradeHubStrategy.OnExecutionArrived(execution);
            PersistencePublisher.PublishDataForPersistence(execution.Fill);
            PersistencePublisher.PublishDataForPersistence(execution.Order);
        }

        /// <summary>
        /// Called when New order status received from <see cref="OrderExecutor"/>
        /// </summary>
        /// <param name="order"></param>
        private void OnOrderExecutorNewArrived(Order order)
        {
            _tradeHubStrategy.OnNewArrived(order);
            PersistencePublisher.PublishDataForPersistence(order);
        }

        /// <summary>
        /// Called when bar received from <see cref="MarketDataListener"/>
        /// </summary>
        /// <param name="bar"></param>
        private void OnBarArrived(Bar bar)
        {
            if (_asyncClassLogger.IsDebugEnabled)
            {
                _asyncClassLogger.Debug(bar.ToString(), _type.FullName, "OnBarArrived");
            }
            _orderExecutor.BarArrived(bar);
            _tradeHubStrategy.OnBarArrived(bar);
        }

        /// <summary>
        /// Called when tick received from <see cref="MarketDataListener"/>
        /// </summary>
        /// <param name="tick"></param>
        private void OnTickArrived(Tick tick)
        {
            if (_asyncClassLogger.IsDebugEnabled)
            {
                _asyncClassLogger.Debug(tick.ToString(), _type.FullName, "OnTickArrived");
            }

            _orderExecutor.TickArrived(tick);
            _tradeHubStrategy.OnTickArrived(tick);
        }

        #endregion

        /// <summary>
        /// Called when Custom Strategy Running status changes
        /// </summary>
        /// <param name="status">Indicate whether the strategy is running or nor</param>
        private void OnStrategyStatusChanged(bool status)
        {
            if (status)
            {
                //_strategyStatus = Infrastructure.Constants.StrategyStatus.Executing;
            }
            else
            {
                //_strategyStatus = Infrastructure.Constants.StrategyStatus.Executed;
            }

            if (_statusChanged != null)
            {
                _statusChanged(status, _strategyKey);
            }
        }

        /// <summary>
        /// Called when Custom Strategy receives new execution message
        /// </summary>
        /// <param name="execution">Contains Execution Info</param>
        private void OnNewExecutionReceived(Execution execution)
        {
            if (_executionReceived != null)
            {
                _executionReceived(execution);
                //Task.Factory.StartNew(() => _executionReceived(execution));
            }

            // Update Stats
            //UpdateStatistics(execution);
            //_statistics.MatlabStatisticsFunction(execution);
            UpdateStatistics(execution);
        }

        /// <summary>
        /// Updates strategy statistics on each execution
        /// </summary>
        /// <param name="execution">Contains Execution Info</param>
        [MethodImpl(MethodImplOptions.Synchronized)]
        private void UpdateStatistics(Execution execution)
        {
            try
            {
                if (_asyncClassLogger.IsDebugEnabled)
                {
                    _asyncClassLogger.Debug("Updating statistics on: " + execution, _type.FullName, "UpdateStatistics");
                }

                // Update statistics on BUY Order
                if (execution.Fill.ExecutionSide.Equals(OrderSide.BUY))
                {
                }
                // Update statistics on SELL Order
                else if (execution.Fill.ExecutionSide.Equals(OrderSide.SELL) ||
                    execution.Fill.ExecutionSide.Equals(OrderSide.SHORT))
                {
                }
                // Update statistics on COVER Order (order used to close the open position)
                else if (execution.Fill.ExecutionSide.Equals(OrderSide.COVER))
                {
                }
            }
            catch (Exception exception)
            {
                _asyncClassLogger.Error(exception, _type.FullName, "UpdateStatistics");
            }
        }

        /// <summary>
        /// Disposes strategy objects
        /// </summary>
        public void Close()
        {
            try
            {
                if (_tradeHubStrategy != null)
                {
                    _dataHandler.Shutdown();
                    _tradeHubStrategy.Dispose();
                    _tradeHubStrategy = null;
                }
            }
            catch (Exception exception)
            {
                _asyncClassLogger.Error(exception, _type.FullName, "Close");
            }
        }
    }
}
