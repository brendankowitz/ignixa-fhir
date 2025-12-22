// @ts-check
import {themes as prismThemes} from 'prism-react-renderer';

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'Ignixa FHIR',
  tagline: 'High-Performance, Multi-Tenant, Cloud-Native FHIR Server for .NET',
  favicon: 'img/favicon.ico',

  future: {
    v4: true,
  },

  url: 'https://brendankowitz.github.io',
  baseUrl: '/ignixa-fhir/',
  trailingSlash: false,

  organizationName: 'brendankowitz',
  projectName: 'ignixa-fhir',

  onBrokenLinks: 'throw',

  markdown: {
    hooks: {
      onBrokenMarkdownLinks: 'warn',
    },
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          sidebarPath: './sidebars.js',
          editUrl: 'https://github.com/brendankowitz/ignixa-fhir/tree/main/docs/site/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      }),
    ],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      image: 'img/logo.png',
      colorMode: {
        defaultMode: 'light',
        disableSwitch: false,
        respectPrefersColorScheme: true,
      },
      navbar: {
        title: 'Ignixa FHIR',
        logo: {
          alt: 'Ignixa Logo',
          src: 'img/logo.png',
        },
        items: [
          {
            type: 'docSidebar',
            sidebarId: 'gettingStartedSidebar',
            position: 'left',
            label: 'Getting Started',
          },
          {
            type: 'docSidebar',
            sidebarId: 'serverSidebar',
            position: 'left',
            label: 'Server',
          },
          {
            type: 'docSidebar',
            sidebarId: 'coreSdkSidebar',
            position: 'left',
            label: 'Core SDK',
          },
          {
            type: 'docSidebar',
            sidebarId: 'adrSidebar',
            position: 'left',
            label: 'ADRs',
          },
          {
            href: 'https://github.com/brendankowitz/ignixa-fhir',
            label: 'GitHub',
            position: 'right',
          },
        ],
      },
      footer: {
        style: 'dark',
        links: [
          {
            title: 'Documentation',
            items: [
              {
                label: 'Getting Started',
                to: '/docs/getting-started/installation',
              },
              {
                label: 'Server Features',
                to: '/docs/server/overview',
              },
              {
                label: 'Core SDK',
                to: '/docs/core-sdk/overview',
              },
            ],
          },
          {
            title: 'Resources',
            items: [
              {
                label: 'FHIR R4',
                href: 'https://hl7.org/fhir/R4/',
              },
              {
                label: 'FHIR R5',
                href: 'https://hl7.org/fhir/R5/',
              },
              {
                label: 'NuGet Packages',
                href: 'https://www.nuget.org/packages?q=Ignixa',
              },
            ],
          },
          {
            title: 'More',
            items: [
              {
                label: 'GitHub',
                href: 'https://github.com/brendankowitz/ignixa-fhir',
              },
              {
                label: 'Docker Image',
                href: 'https://github.com/brendankowitz/packages/container/package/ignixa-fhir',
              },
            ],
          },
        ],
        copyright: `Copyright © ${new Date().getFullYear()} Ignixa FHIR. Built with Docusaurus.`,
      },
      prism: {
        theme: prismThemes.github,
        darkTheme: prismThemes.dracula,
        additionalLanguages: ['csharp', 'json', 'bash', 'powershell', 'yaml', 'docker', 'http'],
      },
      // Uncomment and configure Algolia DocSearch when approved for open source
      // algolia: {
      //   appId: 'YOUR_APP_ID',
      //   apiKey: 'YOUR_SEARCH_API_KEY',
      //   indexName: 'ignixa-fhir',
      //   contextualSearch: true,
      //   searchPagePath: 'search',
      // },
    }),
};

export default config;
