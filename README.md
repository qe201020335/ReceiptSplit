# ReceiptSplit

> [!WARNING]
> This is completely vibe-coded. I (qe201020335) have not written a single line of code in this project.
>
> Use at your own risk!

A web app for splitting a shopping receipt between people. Upload a photo of the receipt, a local vision model
reads the lines, and the app checks them against the receipt's own subtotal, tax and total. Anything misread can be
corrected by hand. Once the receipt checks out, give each item to whoever pays for it, by shares or by dollar
amounts, and copy a summary of who owes what.

It has been tuned on receipts from Costco and T&T but should work with any generic receipt.

## Stack

- ASP.NET Core 10 with EF Core and SQLite, logging to the console with Serilog
- React, TypeScript and Vite with Mantine
- An OpenAI-compatible [llama.cpp](https://github.com/ggml-org/llama.cpp) server with a vision model

## Running it

With the .NET 10 SDK and Node.js installed, and the model server set in `ReceiptSplit/appsettings.json`:

```sh
dotnet run --project ReceiptSplit
```

Then open <http://localhost:5173>. To host it, use `compose.yaml`. The app won't start unless the model server
answers and offers the configured model.
