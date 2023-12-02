import { OcadDirectoryHelper } from './project-directory-selector.helper';

describe('OcadDirectoryHelper', () => {
  const testCases: DirectoryTestNames[] = [
    { name: 'V1', isValid: true },
    { name: 'V10', isValid: true },
    { name: 'V150', isValid: true },
    { name: 'V-1', isValid: false },
    { name: 'V', isValid: false },
    { name: '', isValid: false },
    { name: undefined, isValid: false },
    { name: 'undefined', isValid: false },
    { name: 'null', isValid: false },
    { name: 'V1a', isValid: false },
    { name: 'v1', isValid: false },
    { name: 'VE1', isValid: false },
    { name: 'V1v', isValid: false },
    { name: 'file.ocd', isValid: false },
  ];

  testCases.forEach((test) => {
    it(`DirectoryNames ${test.name} is ${
      test.isValid ? 'valid' : 'invalid'
    }`, () => {
      expect(OcadDirectoryHelper.isValidReleaseFolderName(test?.name)).toEqual(
        test.isValid
      );
    });
  });
});

interface DirectoryTestNames {
  name?: string;
  isValid: boolean;
}
