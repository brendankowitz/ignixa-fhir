import clsx from 'clsx';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

const FeatureList = [
  {
    title: 'Multi-Version FHIR Support',
    emoji: '🏥',
    description: (
      <>
        Full support for FHIR R4, R4B, R5, R6, and STU3. Seamlessly handle multiple
        FHIR versions in a single deployment with version-aware routing.
      </>
    ),
  },
  {
    title: 'High Performance',
    emoji: '⚡',
    description: (
      <>
        Built on .NET 9 with streaming-first architecture, zero-copy serialization,
        and compiled FHIRPath expressions for minimal memory footprint.
      </>
    ),
  },
  {
    title: 'Multi-Tenant Ready',
    emoji: '🏢',
    description: (
      <>
        Physical data isolation between tenants with per-tenant configuration.
        Deploy once, serve many healthcare organizations securely.
      </>
    ),
  },
  {
    title: 'Modular Core SDK',
    emoji: '📦',
    description: (
      <>
        Use the complete server or pick individual NuGet packages. FHIRPath, 
        Validation, Search, Serialization—all available as standalone libraries.
      </>
    ),
  },
  {
    title: 'Three-Tier Validation',
    emoji: '✅',
    description: (
      <>
        Choose your validation level: Fast (structural), Spec (FHIR compliance),
        or Profile (full StructureDefinition validation with terminology).
      </>
    ),
  },
  {
    title: 'Cloud Native',
    emoji: '☁️',
    description: (
      <>
        Deploy to Azure, Docker, or Kubernetes. Includes Bicep templates, 
        Managed Identity support, and SQL Server / Blob Storage backends.
      </>
    ),
  },
];

function Feature({emoji, title, description}) {
  return (
    <div className={clsx('col col--4')}>
      <div className="text--center">
        <span style={{fontSize: '3rem'}}>{emoji}</span>
      </div>
      <div className="text--center padding-horiz--md">
        <Heading as="h3">{title}</Heading>
        <p>{description}</p>
      </div>
    </div>
  );
}

export default function HomepageFeatures() {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className="row">
          {FeatureList.map((props, idx) => (
            <Feature key={idx} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}
