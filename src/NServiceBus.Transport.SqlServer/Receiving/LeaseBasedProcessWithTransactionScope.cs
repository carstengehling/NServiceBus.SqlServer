﻿namespace NServiceBus.Transport.SqlServer
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Transactions;

    class LeaseBasedProcessWithTransactionScope : ReceiveStrategy
    {
        public LeaseBasedProcessWithTransactionScope(TransactionOptions transactionOptions, SqlConnectionFactory connectionFactory, FailureInfoStorage failureInfoStorage, TableBasedQueueCache tableBasedQueueCache)
         : base(tableBasedQueueCache)
        {
            this.transactionOptions = transactionOptions;
            this.connectionFactory = connectionFactory;
            this.failureInfoStorage = failureInfoStorage;
        }

        public override async Task ReceiveMessage(CancellationTokenSource receiveCancellationTokenSource)
        {
            Message message = null;
            try
            {
                using (var scope = new TransactionScope(TransactionScopeOption.RequiresNew, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
                {
                    using (var connection = await connectionFactory.OpenNewConnection().ConfigureAwait(false))
                    {
                        message = await TryReceive(connection, null, receiveCancellationTokenSource).ConfigureAwait(false);

                        scope.Complete();

                        if (message == null)
                        {
                            // The message was received but is not fit for processing (e.g. was DLQd).
                            // In such a case we still need to commit the transport tx to remove message
                            // from the queue table.
                            return;
                        }

                        connection.Close();
                    }
                }

                var processingSuccessful = false;

                using (var scope = new TransactionScope(TransactionScopeOption.RequiresNew, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
                {
                    if (await TryProcess(message, PrepareTransportTransaction()).ConfigureAwait(false))
                    {
                        using (var connection = await connectionFactory.OpenNewConnection().ConfigureAwait(false))
                        {
                            if (await TryDeleteLeasedRow(message.LeaseId.Value, connection, null).ConfigureAwait(false))
                            {
                                processingSuccessful = true;
                            }
                        }

                        if (processingSuccessful)
                        {
                            scope.Complete();
                        }
                    }
                }

                if(processingSuccessful == false)
                { 
                    using (var releaseScope = new TransactionScope(TransactionScopeOption.RequiresNew, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
                    using (var connection = await connectionFactory.OpenNewConnection().ConfigureAwait(false))
                    {
                        await ReleaseLease(message.LeaseId.Value, connection, null).ConfigureAwait(false);

                        releaseScope.Complete();
                    }
                }
                else
                {
                    failureInfoStorage.ClearFailureInfoForMessage(message.TransportId);
                }
            }
            catch (Exception exception)
            {
                if (message == null)
                {
                    throw;
                }
                failureInfoStorage.RecordFailureInfoForMessage(message.TransportId, exception);
            }
        }

        TransportTransaction PrepareTransportTransaction()
        {
            var transportTransaction = new TransportTransaction();

            //these resources are meant to be used by anyone except message dispatcher e.g. persister
            transportTransaction.Set(Transaction.Current);

            return transportTransaction;
        }

        async Task<bool> TryProcess(Message message, TransportTransaction transportTransaction)
        {
            if (failureInfoStorage.TryGetFailureInfoForMessage(message.TransportId, out var failure))
            {
                var errorHandlingResult = await HandleError(failure.Exception, message, transportTransaction, failure.NumberOfProcessingAttempts).ConfigureAwait(false);

                if (errorHandlingResult == ErrorHandleResult.Handled)
                {
                    return true;
                }
            }

            try
            {
                return await TryProcessingMessage(message, transportTransaction).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failureInfoStorage.RecordFailureInfoForMessage(message.TransportId, exception);
                return false;
            }
        }

        TransactionOptions transactionOptions;
        SqlConnectionFactory connectionFactory;
        FailureInfoStorage failureInfoStorage;
    }
}