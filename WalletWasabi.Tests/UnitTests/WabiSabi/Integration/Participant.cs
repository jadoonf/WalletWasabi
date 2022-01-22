using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Models;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WebClients.Wasabi;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Integration;

internal class Participant: IDisposable
{
	private bool _disposedValue;

	public Participant(string name, IRPCClient rpc, IWasabiHttpClientFactory httpClientFactory)
	{
		HttpClientFactory = httpClientFactory;

		Wallet = new TestWallet(name, rpc);
	}

	private TestWallet Wallet { get; }
	public IWasabiHttpClientFactory HttpClientFactory { get; }
	private SmartTransaction? SplitTransaction { get; set; }

	public async Task GenerateSourceCoinAsync(CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		await Wallet.GenerateAsync(1, cancellationToken).ConfigureAwait(false);
	}

	public async Task GenerateCoinsAsync(int numberOfCoins, int seed, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		var feeRate = new FeeRate(4.0m);
		var splitTx = Wallet.CreateSelfTransfer(FeeRate.Zero);
		var satoshisAvailable = splitTx.Outputs[0].Value.Satoshi;
		splitTx.Outputs.Clear();

		var rnd = new Random(seed);
		double NextNotTooSmall() => 0.00001 + (rnd.NextDouble() * 0.99999);
		var sampling = Enumerable
			.Range(0, numberOfCoins - 1)
			.Select(_ => NextNotTooSmall())
			.Prepend(0)
			.Prepend(1)
			.OrderBy(x => x)
			.ToArray();

		var amounts = sampling
			.Zip(sampling.Skip(1), (x, y) => y - x)
			.Select(x => x * satoshisAvailable)
			.Select(x => Money.Satoshis((long)x));

		var scriptPubKey = Wallet.ScriptPubKey;
		foreach (var amount in amounts)
		{
			var effectiveOutputValue = amount - feeRate.GetFee(scriptPubKey.EstimateOutputVsize());
			splitTx.Outputs.Add(new TxOut(effectiveOutputValue, scriptPubKey));
		}

		await Wallet.SendRawTransactionAsync(Wallet.SignTransaction(splitTx), cancellationToken).ConfigureAwait(false);
		SplitTransaction = new SmartTransaction(splitTx, new Height(1));
	}

	public async Task StartParticipatingAsync(CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		var apiClient = new WabiSabiHttpApiClient(HttpClientFactory.NewHttpClientWithDefaultCircuit());
		using var roundStateUpdater = new RoundStateUpdater(TimeSpan.FromSeconds(3), apiClient);
		await roundStateUpdater.StartAsync(cancellationToken).ConfigureAwait(false);

		var coinJoinClient = new CoinJoinClient(
			HttpClientFactory,
			Wallet,
			Wallet,
			roundStateUpdater,
			consolidationMode: true);

		// Run the coinjoin client task.
		var walletHdPubKey = new HdPubKey(Wallet.PubKey, KeyPath.Parse("m/84'/0/0/0/0"), SmartLabel.Empty, KeyState.Clean);
		walletHdPubKey.SetAnonymitySet(1); // bug if not settled

		if (SplitTransaction != null)
		{
			throw new InvalidOperationException($"{nameof(GenerateCoinsAsync)} has to be called first.");
		}

		var smartCoins = SplitTransaction.Transaction.Outputs.AsIndexedOutputs()
			.Select(x => new SmartCoin(SplitTransaction, x.N, walletHdPubKey))
			.ToList();

		// Run the coinjoin client task.
		await coinJoinClient.StartCoinJoinAsync(smartCoins, cancellationToken).ConfigureAwait(false);

		await roundStateUpdater.StopAsync(cancellationToken).ConfigureAwait(false);
	}

	private void ThrowIfDisposed()
	{
		if (_disposedValue)
		{
			throw new ObjectDisposedException(nameof(TestWallet));
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			Wallet.Dispose();
			_disposedValue = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
