import { Dictionary } from 'lodash'
import langNO from '../../../assets/i18n/no.json'
import langEN from '../../../assets/i18n/en.json'

const langs: Dictionary<Dictionary<string>> = {
    NO: langNO,
    EN: langEN,
}

describe('Translation tests', () => {
    it('All languages should have same number of translations', () => {
        const expectedNumberOfTranslations = Object.keys(langs['NO']).length
        Object.keys(langs)
            .filter((lang) => lang !== 'NO')
            .forEach((langTag) => {
                expect(Object.keys(langs[langTag]).length).toBe(
                    expectedNumberOfTranslations
                )
            })
    })

    Object.keys(langs).forEach((langTagToCheck) => {
        const translationDictToCheck: Dictionary<string> = langs[langTagToCheck]
        const translationKeysToCheck = Object.keys(translationDictToCheck)

        describe(`All '${langTagToCheck}' translations should exist in other languages`, () => {
            Object.keys(langs)
                .filter((tag) => langTagToCheck !== tag)
                .forEach((languageTagToCompare) => {
                    const translationsToCompare = langs[languageTagToCompare]
                    const translationKeysToCompare = Object.keys(
                        translationsToCompare
                    )

                    translationKeysToCheck.forEach((key) => {
                        it(`Translation '${key}' exists in ${langTagToCheck}. Should therefore exist in '${languageTagToCompare}'`, () => {
                            const origTranslation = translationDictToCheck[key]
                            const comparedTranslation =
                                translationsToCompare[key]
                            //   expect(origTranslation).not.toBe(comparedTranslation);

                            expect(origTranslation.length).toBeGreaterThan(0)
                            expect(comparedTranslation.length).toBeGreaterThan(
                                0
                            )

                            expect(
                                comparedTranslation.length
                            ).toBeGreaterThanOrEqual(1)
                            expect(translationKeysToCompare.includes(key)).toBe(
                                true
                            )
                        })
                    })
                })
        })
    })
})
