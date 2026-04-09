## Storing / accessing secrets

Secrets used by Angular (e.g. AppInsights keys) are stored in Azure App Configuration and accessed through its [npm package](https://www.npmjs.com/package/@azure/app-configuration).
Storing the key for the Azure App Configuration is done in the .env file at the project root, which is not included in source control. Following the setup suggested here: https://betterprogramming.pub/how-to-secure-angular-environment-variables-for-use-in-github-actions-39c07587d590
