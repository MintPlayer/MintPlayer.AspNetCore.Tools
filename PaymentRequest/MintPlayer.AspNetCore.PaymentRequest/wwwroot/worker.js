self.addEventListener("paymentrequest", function (event) {
    console.warn("b");
    // Handle the payment request
    event.respondWith(handlePaymentRequest(event));
});

self.addEventListener("canmakepayment", function (event) {
    console.warn("a");
    event.respondWith(
        new Promise(function (resolve, reject) { resolve(true); })
    );
});

function handlePaymentRequest(event) {
    return new Promise(function (resolve, reject) {
        // Extract payment details
        const methodData = event.methodData;
        const details = event.total;
        const modifiers = event.modifiers;

        // Process the payment (e.g., show a custom payment UI)
        // For demonstration, we'll return a dummy response

        console.warn("methodData", methodData);
        resolve({
            methodName: methodData[0].supportedMethods,
            details: {
                confirmationNumber: '1234567890', // Custom response data
            },
        });
    });
}