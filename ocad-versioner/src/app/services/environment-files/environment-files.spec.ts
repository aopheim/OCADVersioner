import { environment as prodEnvironments } from '../../../environments/environment.prod';

describe('Environment files should not contain secrets', () => {
  const prodEnvs = prodEnvironments;
  it('Prod Environment file should have default values', () => {
    expect(prodEnvs.APPINSIGHTSINSTRUMENTATIONKEY).toBe('TEST');
    expect(prodEnvs.production).toBe(true);
  });
});
