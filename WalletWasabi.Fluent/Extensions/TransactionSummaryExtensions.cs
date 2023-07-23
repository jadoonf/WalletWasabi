using NBitcoin;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Models;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Extensions;

public static class TransactionSummaryExtensions
{
	public static bool IsConfirmed(this TransactionSummary model)
	{
		var confirmations = model.GetConfirmations();
		return confirmations > 0;
	}

	public static TimeSpan? ConfirmationTime(this TransactionSummary transactionSummary, Wallet wallet)
	{
		if (transactionSummary.FeeRate is { } feeRate)
		{
			var time = TransactionFeeHelper.CalculateConfirmationTime(feeRate, wallet);
			if (time < TimeSpan.FromMinutes(1))
			{
				return null;
			}

			return time;
		}

		return null;
	}

	public static int GetConfirmations(this TransactionSummary model) => model.Height.Type == HeightType.Chain ? (int)Services.BitcoinStore.SmartHeaderChain.TipHeight - model.Height.Value + 1 : 0;

	public static MoneyUnit ToMoneyUnit(this FeeDisplayUnit feeDisplayUnit) =>
		feeDisplayUnit switch
		{
			FeeDisplayUnit.BTC => MoneyUnit.BTC,
			FeeDisplayUnit.Satoshis => MoneyUnit.Satoshi,
			_ => throw new InvalidOperationException($"Invalid Fee Display Unit value: {feeDisplayUnit}")
		};

	public static string ToFeeDisplayUnitRawString(this Money? fee)
	{
		if (fee is null)
		{
			return "Unknown";
		}

		var displayUnit = Services.UiConfig.FeeDisplayUnit.GetEnumValueOrDefault(FeeDisplayUnit.BTC);

		return displayUnit switch
		{
			FeeDisplayUnit.Satoshis => fee.Satoshi.ToString(),
			_ => fee.ToString()
		};
	}

	public static string ToFeeDisplayUnitFormattedString(this Money? fee)
	{
		if (fee is null)
		{
			return "Unknown";
		}

		var displayUnit = Services.UiConfig.FeeDisplayUnit.GetEnumValueOrDefault(FeeDisplayUnit.BTC);
		var moneyUnit = displayUnit.ToMoneyUnit();

		var feePartText = moneyUnit switch
		{
			MoneyUnit.BTC => fee.ToFormattedString(),
			MoneyUnit.Satoshi => fee.Satoshi.ToString(),
			_ => fee.ToString()
		};

		var feeText = $"{feePartText} {displayUnit.FriendlyName()}";

		return feeText;
	}
}
