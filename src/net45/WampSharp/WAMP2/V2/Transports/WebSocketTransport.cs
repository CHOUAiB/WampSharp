using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using WampSharp.Logging;
using WampSharp.Core.Listener;

using WampSharp.V2.Binding;
using WampSharp.V2.Binding.Transports;

namespace WampSharp.V2.Transports
{
    /// <summary>
    /// Represents a WebSocket transport.
    /// </summary>
    public abstract class WebSocketTransport<TConnection> : IWampTransport
    {
        protected readonly ILog mLogger;

        private readonly IDictionary<string, ConnectionListener> mBindings =
            new Dictionary<string, ConnectionListener>();

        public WebSocketTransport()
        {
            mLogger = LogProvider.GetLogger(this.GetType());
        }

        #region Protected Members

        /// <summary>
        /// Gets the subprotocols registered within this transport.
        /// </summary>
        protected string[] SubProtocols
        {
            get { return mBindings.Keys.ToArray(); }
        }

        /// <summary>
        /// Call this when a new connection is established.
        /// </summary>
        /// <param name="connection">The new connection.</param>
        protected void OnNewConnection(TConnection connection)
        {
            string protocol = GetSubProtocol(connection);

            ConnectionListener listener;

            if (mBindings.TryGetValue(protocol, out listener))
            {
                listener.OnNewConnection(connection);
            }
            else
            {
                mLogger.ErrorFormat("No handler registered for protocol '{0}'",
                    protocol);
            }
        }

        #endregion

        /// <summary>
        /// <see cref="IDisposable.Dispose"/>
        /// </summary>
        public abstract void Dispose();

        void IDisposable.Dispose()
        {
            foreach (ConnectionListener connectionListener in mBindings.Values)
            {
                connectionListener.Dispose();
            }

            this.Dispose();
        }

        #region Abstract Methods

        /// <summary>
        /// Occurs after a WampConnection has been created.
        /// </summary>
        /// <remarks>Override this in order to open your connection.</remarks>
        protected abstract void OpenConnection<TMessage>
            (IWampConnection<TMessage> connection);

        /// <summary>
        /// Gets the sub-protocol associated with the given connection.
        /// </summary>
        /// <param name="connection">The given connection.</param>
        /// <returns>The sub-protocol associated with the given connection</returns>
        protected abstract string GetSubProtocol(TConnection connection);

        /// <summary>
        /// Creates a binary <see cref="IWampConnection{TMessage}"/> using the given 
        /// binding and the given connection.
        /// </summary>
        /// <param name="connection">The given connection.</param>
        /// <param name="binding">The given binding.</param>
        protected abstract IWampConnection<TMessage> CreateBinaryConnection<TMessage>
            (TConnection connection, IWampBinaryBinding<TMessage> binding);

        /// <summary>
        /// Creates a text <see cref="IWampConnection{TMessage}"/> using the given 
        /// binding and the given connection.
        /// </summary>
        /// <param name="connection">The given connection.</param>
        /// <param name="binding">The given binding.</param>
        protected abstract IWampConnection<TMessage> CreateTextConnection<TMessage>
            (TConnection connection, IWampTextBinding<TMessage> binding);

        public abstract void Open();

        #endregion

        #region GetListener Methods

        public IWampConnectionListener<TMessage> GetListener<TMessage>(IWampBinding<TMessage> binding)
        {
            IWampTextBinding<TMessage> textBinding = binding as IWampTextBinding<TMessage>;

            if (textBinding != null)
            {
                return GetListener(textBinding);
            }

            IWampBinaryBinding<TMessage> binaryBinding = binding as IWampBinaryBinding<TMessage>;

            if (binaryBinding != null)
            {
                return GetListener(binaryBinding);
            }

            throw new ArgumentException("WebSockets can only deal with binary/text transports", "binding");
        }

        private IWampConnectionListener<TMessage> GetListener<TMessage>(IWampTextBinding<TMessage> binding)
        {
            TextConnectionListener<TMessage> listener = new TextConnectionListener<TMessage>(binding, this);

            RegisterBinding(binding, listener);

            return listener;
        }

        private IWampConnectionListener<TMessage> GetListener<TMessage>(IWampBinaryBinding<TMessage> binding)
        {
            BinaryConnectionListener<TMessage> listener = new BinaryConnectionListener<TMessage>(binding, this);

            RegisterBinding(binding, listener);

            return listener;
        }

        private void RegisterBinding(IWampBinding binding, ConnectionListener listener)
        {
            if (mBindings.ContainsKey(binding.Name))
            {
                throw new ArgumentException("Already registered a binding for protocol: " +
                                            binding.Name,
                    "binding");
            }

            mBindings.Add(binding.Name, listener);
        }

        #endregion

        #region Nested classes

        private abstract class ConnectionListener : IDisposable
        {
            public abstract void OnNewConnection(TConnection connection);
            public abstract void Dispose();
        }

        private abstract class ConnectionListener<TMessage> : ConnectionListener,
            IWampConnectionListener<TMessage>
        {
            private readonly Subject<IWampConnection<TMessage>> mSubject =
                new Subject<IWampConnection<TMessage>>();

            private readonly WebSocketTransport<TConnection> mParent;

            protected ConnectionListener(WebSocketTransport<TConnection> parent)
            {
                mParent = parent;
            }

            public WebSocketTransport<TConnection> Parent
            {
                get { return mParent; }
            }

            protected void OnNewConnection(IWampConnection<TMessage> connection)
            {
                mSubject.OnNext(connection);
                mParent.OpenConnection(connection);
            }

            public IDisposable Subscribe(IObserver<IWampConnection<TMessage>> observer)
            {
                return mSubject.Subscribe(observer);
            }

            public override void Dispose()
            {
                mSubject.OnCompleted();
                mSubject.Dispose();
            }
        }

        private class BinaryConnectionListener<TMessage> : ConnectionListener<TMessage>
        {
            private readonly IWampBinaryBinding<TMessage> mBinding;

            public BinaryConnectionListener
                (IWampBinaryBinding<TMessage> binding,
                    WebSocketTransport<TConnection> parent)
                : base(parent)
            {
                mBinding = binding;
            }

            public IWampBinaryBinding<TMessage> Binding
            {
                get { return mBinding; }
            }

            public override void OnNewConnection(TConnection connection)
            {
                OnNewConnection(Parent.CreateBinaryConnection(connection, Binding));
            }
        }

        private class TextConnectionListener<TMessage> : ConnectionListener<TMessage>
        {
            private readonly IWampTextBinding<TMessage> mBinding;

            public TextConnectionListener
                (IWampTextBinding<TMessage> binding,
                    WebSocketTransport<TConnection> parent) :
                        base(parent)
            {
                mBinding = binding;
            }

            public IWampTextBinding<TMessage> Binding
            {
                get { return mBinding; }
            }

            public override void OnNewConnection(TConnection connection)
            {
                OnNewConnection(Parent.CreateTextConnection(connection, Binding));
            }
        }

        #endregion
    }
}