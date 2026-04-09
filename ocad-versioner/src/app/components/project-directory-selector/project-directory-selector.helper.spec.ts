import { OcadDirectoryHelper } from './project-directory-selector.helper'

describe('OcadDirectoryHelper', () => {
    const versionNameTestCases: DirectoryTestNames[] = [
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
    ]

    versionNameTestCases.forEach((test) => {
        it(`DirectoryNames ${test.name} is ${
            test.isValid ? 'valid' : 'invalid'
        }`, () => {
            expect(
                OcadDirectoryHelper.isValidReleaseFolderName(test?.name)
            ).toEqual(test.isValid)
        })
    })

    const versionNumberTestCases: VersionNumberTestCase[] = [
        { versionName: 'V1', versionNumber: 1 },
        { versionName: 'v1', versionNumber: null },
        { versionName: 'V15', versionNumber: 15 },
        { versionName: '', versionNumber: null },
        { versionName: 'V', versionNumber: null },
        { versionName: 'V1a', versionNumber: null },
    ]
    versionNumberTestCases.forEach((test) => {
        it(`VersionName ${test.versionName} should be read as number ${test.versionNumber}`, () => {
            expect(
                OcadDirectoryHelper.getVersionNumberFromVersionName(
                    test?.versionName
                )
            ).toEqual(test.versionNumber)
        })
    })
})

interface DirectoryTestNames {
    name?: string
    isValid: boolean
}

interface VersionNumberTestCase {
    versionName: string
    versionNumber: number | null
}
