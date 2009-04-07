using System;
using System.Diagnostics;
using System.Runtime.Remoting;
using System.Threading;

namespace Palaso.Services.ForServers
{
	/// <summary>
	/// Use this class to export services from a user application.  It helps
	/// with handling the states of being invisible, becoming visible, and
	/// ensuring that only one instance of the application is running.
	/// </summary>
	public class ServiceAppSingletonHelper : IDisposable
	{
		public enum State
		{
			Starting,
			ServerMode,
			UiMode,
			Exitting,
			UiModeStarting
		} ;

		private IDisposable _ipcServiceProviderDescriptor = null;

		private State _state;
		private State _requestedState;
		private static ServiceAppConnector _connector;

		private readonly string _serviceName;
		private bool _couldHaveTwinsInProcess;
		public event EventHandler BringToFrontRequest;

		/// <summary>
		/// Attempts to locate an already-running instance of the program with the given pipe
		/// (e.g., open on the same document).  If it finds one, it asks it to come to the front.
		/// If it can't find one, it claims that pipe and returns a new ServiceAppSingletonHelper
		/// </summary>
		/// <param name="serviceName"></param>
		/// <param name="startInServerMode"></param>
		/// <returns>null if an application is already open with that pipe, otherwise a helper object</returns>
		public static ServiceAppSingletonHelper CreateServiceAppSingletonHelperIfNeeded(string serviceName, bool startInServerMode, bool couldHaveTwinsInProcess)
		{
			ServiceAppSingletonHelper helper = new ServiceAppSingletonHelper(serviceName, startInServerMode, couldHaveTwinsInProcess);
			if (!helper.StartupIfAppropriate())
			{
				helper.Dispose();
				return null;
			}
			return helper;
		}

		public static ServiceAppSingletonHelper CreateServiceAppSingletonHelperIfNeeded(string serviceName, bool startInServerMode)
		{
			return CreateServiceAppSingletonHelperIfNeeded(serviceName, startInServerMode, false);
		}

		/// <summary>
		///
		/// </summary>
		/// <param name="serviceName"></param>
		/// <param name="startInServerMode"></param>
		/// <param name="couldHaveTwinsInProcess">true only for tests where we can have multiple threads opening processes to simulate multiple apps</param>
		private ServiceAppSingletonHelper(string serviceName, bool startInServerMode, bool couldHaveTwinsInProcess)
		{

			if (string.IsNullOrEmpty(serviceName))
			{
				throw new ArgumentException("serviceName");
			}

			_serviceName = serviceName;
			_couldHaveTwinsInProcess = couldHaveTwinsInProcess;
			_state = State.Starting;

			_requestedState = startInServerMode ? State.ServerMode : State.UiMode;

		}

		public State CurrentState
		{
			get { return _state; }
		}

		public void UiReadyForEvents()
		{
			_state = State.UiMode;
			if (BringToFrontRequest != null)
			{
				BringToFrontRequest.Invoke(this, null);
			}
		}

		public void EnsureUIRunningAndInFront()
		{
			if (_state == State.UiMode)
			{
				On_BringToFrontRequest(this, null);
				return;
			}
			_requestedState = State.UiMode;
			for (int i = 0; i < 100; i++) // wait 5 seconds
			{
				if (_state == State.UiMode || _state == State.Exitting)
				{
					break;
				}
				Thread.Sleep(50);
			}
			//wait another second to allow the apps event hander to truly be running
			Thread.Sleep(1000);
			switch (_state)
			{
				case State.Exitting:
					throw new Exception(Process.GetCurrentProcess().ProcessName + " is in the process of Exitting.");
				case State.UiMode:
					return;
				case State.Starting:
					throw new Exception(
						string.Format("Gave up trying to get {0} To switch to ui mode (still Starting app).",
									  Process.GetCurrentProcess().ProcessName));
				case State.UiModeStarting:
					throw new Exception(
						string.Format("Gave up trying to get {0} To switch to ui mode (still Starting UI).",
									  Process.GetCurrentProcess().ProcessName));
				case State.ServerMode:
					throw new Exception(
						string.Format("Gave up trying to get {0} To switch to ui mode (still in server mode).",
									  Process.GetCurrentProcess().ProcessName));
			}
		}

		public void OnExitIfInServerMode(object sender, EventArgs e)
		{
			_requestedState = State.Exitting;
		}


		/// <summary>
		/// Decides whether there is already an app using this service name, and claims the service name if not.
		/// </summary>
		/// <returns>false if this application should just exit</returns>
		private bool StartupIfAppropriate()
		{
			Mutex preventMultipleStartups = new Mutex(false, "Palaso.Services.ServiceAppSingletonHelper.StartupIfAppropriate");
			if (!preventMultipleStartups.WaitOne(10000, false))
			{
				throw new ApplicationException("Timed out while waiting to be able to create service.");
			}
			//            Process[] twins = System.Diagnostics.Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName);
			//            if (_couldHaveTwinsInProcess || twins.Length > 1)
			try
			{
				Debug.WriteLine("Checking to see if there is another app already providing this service...");

				IServiceAppConnectorWithProxy alreadyExistingInstance =
					IpcSystem.GetExistingService<IServiceAppConnectorWithProxy>(_serviceName);
				if ((alreadyExistingInstance != null))
				{
					if (_requestedState == State.UiMode)
					{
						Debug.WriteLine("Found another app already providing this service.");
						alreadyExistingInstance.BringToFront();
					}
					return false;
				}
				else
				{
					Debug.WriteLine("Did not find another app already providing this service.");
				}

				// create the service any future versions of this (with the same serviceName) will use
				// to find out we're already running and tell us other stuff (like BringToFront)
				//Debug.Assert(null == RemotingServices.GetServerTypeForUri(IpcSystem.GetUrlForService(_serviceName, IpcSystem.StartingPort)));

				_connector = new ServiceAppConnector();
				_connector.BringToFrontRequest += On_BringToFrontRequest;
				_ipcServiceProviderDescriptor = IpcSystem.StartServingObject(_serviceName, _connector);
			}
			finally
			{
				preventMultipleStartups.ReleaseMutex();
				preventMultipleStartups.Close();
			}

			return true;
		}


		/// <summary>
		/// This is used to bring an already-in-ui-mode app up to the front by sending a message on to the
		/// form.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		private void On_BringToFrontRequest(object sender, EventArgs args)
		{
			_requestedState = State.UiMode;

			//now tell the form, if it is already existing and has registered for this event

			if (BringToFrontRequest != null)
			{
				BringToFrontRequest.Invoke(this, null);
			}
		}


		public delegate void StartUI();
		public void HandleEventsUntilExit(StartUI uiStarter)
		{
			bool someoneHasAttached = false;
			_state = State.ServerMode;
			while (true)
			{
				if (_connector.ClientIds.Count > 1)
				{
					someoneHasAttached = true;
				}
				//once at least one client has registered (attached), quit
				//when none are attached anymore
				if (someoneHasAttached && _connector.ClientIds.Count == 0)
				{
					_state = State.Exitting;
					break;
				}
				if (_requestedState == State.UiMode)
				{
					_state = State.UiModeStarting;
					uiStarter();//this guy need to call us back with UiReadyForEvents() when ui is fully up.
					// for now, always exit if the user exits the ui (can review this)
					_state = State.Exitting;
					break;
				}
				if (_requestedState == State.Exitting)
				{
					_state = State.Exitting;
					break;
				}
				Thread.Sleep(100);//provide the actual service while we sleep
			}
		}

		//        public static void DisposeForNextTest()
		//        {
		//            if (_connector != null)
		//            {
		////this is just a guess, it's unclear what this does
		//                RemotingServices.Disconnect(_connector);
		//                _connector = null;
		//                IPCUtils.UnregisterHttpChannel(IPCUtils.URLPort);
		//            }
		//        }

		public void TestRequestsExitFromServerMode()
		{
			_requestedState = State.Exitting;
			DateTime start = DateTime.Now;
			while (_state != State.Exitting & DateTime.Now - start < TimeSpan.FromSeconds(1))
			{
				Thread.Sleep(10);
			}
		}

		#region IDisposable Members
#if DEBUG
		~ServiceAppSingletonHelper()
		{
			if (!this._disposed)
			{
				throw new InvalidOperationException("Disposed not explicitly called on " + GetType().FullName + ".");
			}
		}
#endif

		private bool _disposed = false;

		public bool IsDisposed
		{
			get
			{
				return _disposed;
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!IsDisposed)
			{
				if (disposing)
				{
					// dispose-only, i.e. non-finalizable logic
					if (_ipcServiceProviderDescriptor != null)
					{
						_ipcServiceProviderDescriptor.Dispose();
					}
					_ipcServiceProviderDescriptor = null;
				}

				// shared (dispose and finalizable) cleanup logic
				this._disposed = true;
			}
		}

		protected void VerifyNotDisposed()
		{
			if (this._disposed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}
		}

		#endregion
	}
}