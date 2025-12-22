// @ts-check

/**
 * @type {import('@docusaurus/plugin-content-docs').SidebarsConfig}
 */
const sidebars = {
  gettingStartedSidebar: [
    {
      type: 'category',
      label: 'Getting Started',
      link: {
        type: 'generated-index',
        title: 'Getting Started',
        description: 'Learn how to get started with Ignixa FHIR Server and Core SDK.',
      },
      items: [
        'getting-started/installation',
        'getting-started/quick-start',
        'getting-started/configuration',
      ],
    },
  ],

  serverSidebar: [
    {
      type: 'category',
      label: 'Server Overview',
      link: {
        type: 'doc',
        id: 'server/overview',
      },
      items: [
        'server/architecture',
        'server/multi-tenancy',
      ],
    },
    {
      type: 'category',
      label: 'FHIR Compliance',
      items: [
        'server/fhir/capability-statement',
        'server/fhir/supported-resources',
        'server/fhir/search-parameters',
        'server/fhir/operations',
      ],
    },
    {
      type: 'category',
      label: 'Features',
      items: [
        'server/features/validation',
        'server/features/bulk-operations',
        'server/features/subscriptions',
      ],
    },
    {
      type: 'category',
      label: 'Deployment',
      items: [
        'server/deployment/docker',
        'server/deployment/azure',
      ],
    },
    {
      type: 'category',
      label: 'Security',
      items: [
        'server/security/authentication',
        'server/security/authorization',
      ],
    },
  ],

  coreSdkSidebar: [
    {
      type: 'category',
      label: 'Core SDK',
      link: {
        type: 'doc',
        id: 'core-sdk/overview',
      },
      items: [
        'core-sdk/abstractions',
        'core-sdk/serialization',
        'core-sdk/fhirpath',
        'core-sdk/validation',
        'core-sdk/search',
        'core-sdk/fhir-fakes',
        'core-sdk/package-management',
      ],
    },
  ],

  adrSidebar: [
    {
      type: 'category',
      label: 'Architecture Decision Records',
      link: {
        type: 'doc',
        id: 'adr/index',
      },
      items: [
        'adr/adr-2501-authorization',
        'adr/adr-2509-vertical-slice-architecture',
        'adr/adr-2509-inmemory-search',
        'adr/adr-2509-bundle-processing',
        'adr/adr-2510-multi-tenancy',
        'adr/adr-2510-background-jobs',
        'adr/adr-2510-validation-architecture',
      ],
    },
  ],
};

export default sidebars;
