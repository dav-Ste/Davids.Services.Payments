# Davids.Services.Payments

Clean Architecture `.NET 10` asynchronous payment capture microservice for the Davids platform, backed by SQL Server (`paymentsdb`) and RabbitMQ (`orders.order_placed` -> `payments.payment_captured` / `payments.payment_failed`) with zero raw PAN storage.
